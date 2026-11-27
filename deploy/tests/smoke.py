#!/usr/bin/env python3
"""在临时目录中启动发布附件，验证 OpenERP 单容器部署。"""

import argparse
import base64
from contextlib import contextmanager
import http.client
from http.cookies import SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
import secrets
import ssl
import subprocess
import tempfile
from threading import Thread
import time


SERVICE = "openerp"
WEB_PORT = 8000
DATA = "/var/lib/openerp"
POSTGRES = "/var/lib/postgresql"
PG_CREDENTIALS = f"{DATA}/credentials/postgres/current"
EF_BUNDLE = "/app/efbundle"
SPA_MARKER = b'<div id="app">'
GIF = base64.b64decode("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")
SETTINGS = "/api/order-sync/settings"
BUILD_FIELDS = ("version", "commit", "buildTime")


class SmokeError(RuntimeError):
    pass


def check(condition, reason):
    if not condition:
        raise SmokeError(reason)


def docker(*args):
    return subprocess.check_output(["docker", *args], text=True).strip()


def wait_for(check_condition, reason, timeout=120):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = check_condition()
        if result:
            return result
        time.sleep(1)
    raise SmokeError(reason)


class ProxyHandler(BaseHTTPRequestHandler):
    def forward(self):
        # 模拟宿主机 HTTPS 反代。覆盖进入反代的协议头，将请求交给发布容器的 Caddy。
        headers = {
            name: value for name, value in self.headers.items()
            if name.lower() not in {"connection", "x-forwarded-proto", "x-forwarded-for"}
        }
        headers["X-Forwarded-Proto"] = "https"
        headers["X-Forwarded-For"] = self.client_address[0]
        body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        upstream = http.client.HTTPConnection("127.0.0.1", self.server.upstream_port, timeout=5)
        try:
            upstream.request(self.command, self.path, body, headers)
            response = upstream.getresponse()
            data = response.read()
        except (OSError, http.client.HTTPException):
            self.send_error(502)
            return
        finally:
            upstream.close()
        self.send_response_only(response.status)
        for name, value in response.getheaders():
            if name.lower() not in {"connection", "transfer-encoding", "content-length"}:
                self.send_header(name, value)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *_):
        pass

    do_GET = do_POST = do_PUT = forward


@contextmanager
def https_proxy(directory):
    certificate = directory / "localhost.pem"
    key = directory / "localhost.key"
    subprocess.run(
        ["openssl", "req", "-x509", "-newkey", "rsa:2048", "-noenc", "-days", "1",
         "-subj", "/CN=localhost", "-addext", "subjectAltName=IP:127.0.0.1",
         "-keyout", str(key), "-out", str(certificate)],
        check=True, stderr=subprocess.PIPE,
    )
    server_context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    server_context.load_cert_chain(certificate, key)
    trust = ssl.create_default_context(cafile=str(certificate))
    with ThreadingHTTPServer(("127.0.0.1", 0), ProxyHandler) as server:
        server.socket = server_context.wrap_socket(server.socket, server_side=True)
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            yield server, trust
        finally:
            server.shutdown()
            thread.join()


class Deployment:
    def __init__(self, compose_file, directory):
        self.dir = directory
        self.base = [
            "docker", "compose", "--env-file", os.devnull,
            "--project-directory", str(directory), "-f", str(compose_file),
        ]
        self.env = dict(os.environ, WEB_PORT="0")
        self.env.pop("COMPOSE_PROJECT_NAME", None)
        self.override = directory / "smoke.override.json"
        self.configure()

    def compose(self, *arguments, capture=False, check_exit=True, input_text=None):
        return subprocess.run(
            [*self.base, "-f", str(self.override), *arguments],
            env=self.env, check=check_exit, text=True,
            input=input_text,
            stdout=subprocess.PIPE if capture else None,
        ).stdout

    def configure(self, *, image=None, restart=None, bad_bundle=None):
        service = {}
        if image:
            service["image"] = image
        if restart:
            service["restart"] = restart
        if bad_bundle:
            service["volumes"] = [
                {"type": "bind", "source": str(bad_bundle), "target": EF_BUNDLE, "read_only": True}
            ]
        self.override.write_text(json.dumps({"services": {SERVICE: service} if service else {}}))

    def validate_model(self):
        config = json.loads(self.compose("config", "--format", "json", capture=True))
        services = config["services"]
        check(list(services) == [SERVICE],
              f"预期发布模型只有 {SERVICE} 一个服务，实际为 {len(services)} 个：{', '.join(services)}")
        check(config["name"] == self.dir.name, "Compose 项目名未随部署目录变化")
        service = services[SERVICE]
        ports = service.get("ports", [])
        check(len(ports) == 1 and ports[0]["target"] == WEB_PORT and ports[0]["host_ip"] == "127.0.0.1",
              f"预期只在宿主机回环地址发布 Web 端口 {WEB_PORT}")
        check(not service.get("container_name") and not service.get("network_mode") and
              not service.get("privileged"), "测试要求容器名和网络归属当前临时项目")
        check(all(not network.get("external") and network["name"].startswith(f"{self.dir.name}_")
                  for network in config.get("networks", {}).values()),
              "测试要求项目独有的 Compose 网络")
        binds = {mount["target"]: mount for mount in service.get("volumes", [])}
        expected = {DATA: self.dir / "data", POSTGRES: self.dir / "data/postgres"}
        check(binds.keys() == expected.keys() and
              all(binds[target]["type"] == "bind" and Path(binds[target]["source"]).resolve() == source
                  for target, source in expected.items()),
              "测试要求数据目录绑定到当前临时目录的 ./data")
        # Given 空实例目录，When Compose 创建容器，Then 两个宿主机挂载源都须自动创建。
        for mount in binds.values():
            # Compose v5 在 config 输出中把 create_host_path: true 规范化为 bind: {}。
            bind = mount.get("bind")
            check(bind is not None and bind.get("create_host_path", True) is True,
                  f"发布附件未启用宿主机目录自动创建：{mount['source']}")

    def container(self):
        ids = self.compose("ps", "-a", "-q", capture=True).splitlines()
        check(len(ids) == 1, f"预期恰好创建一个 Compose 容器，实际为 {len(ids)} 个")
        return ids[0]

    def inspect(self, fmt):
        return docker("inspect", "--format", fmt, self.container())

    def up(self, *, recreate=False):
        flags = ["--force-recreate"] if recreate else []
        self.compose("up", "-d", "--no-build", *flags)
        self.container()

    def ready(self):
        def ready_client():
            port = self.inspect(
                '{{with (index .NetworkSettings.Ports "%d/tcp")}}{{(index . 0).HostPort}}{{end}}' % WEB_PORT
            )
            if not port:
                return None
            self.proxy.upstream_port = int(port)
            client = Client(self.proxy.server_port, self.trust)
            try:
                if client.request("GET", "/api/auth/status")[0] == 200:
                    return client
            except (OSError, http.client.HTTPException):
                pass

        return wait_for(ready_client, "Web 入口未在预期时间内返回管理员初始化状态")

    def stop(self):
        start = time.monotonic()
        self.compose("stop")
        check(self.inspect("{{.State.ExitCode}}") == "0", "正常停机后容器未以状态码 0 退出")
        return time.monotonic() - start

    def migration_count(self):
        return self.compose(
            "exec", "-T", SERVICE, "psql", "-h", "127.0.0.1", "-U", "postgres", "-d", "openerp",
            "-Atc", 'SELECT count(*) FROM "__EFMigrationsHistory"',
            capture=True,
        ).strip()

    def expect_failed_start(self, cause, logs_pattern):
        # Given 无重启策略的测试实例，When 启动失败，Then 容器退出非零且未进入健康状态。
        self.up()
        wait_for(lambda: self.inspect("{{.State.Status}}") == "exited", f"{cause}后容器没有退出")
        check(self.inspect("{{.State.ExitCode}}") != "0", f"{cause}后容器退出码为 0")
        logs = subprocess.check_output(
            ["docker", "logs", self.container()], text=True, stderr=subprocess.STDOUT,
        )
        check("Now listening on:" not in logs, f"{cause}的启动过程中 API 曾开始监听")
        check(re.search(logs_pattern, logs), f"未找到{cause}的日志")


@contextmanager
def isolated(compose_file):
    with tempfile.TemporaryDirectory(prefix="openerp-smoke-") as temp:
        project = Deployment(compose_file, Path(temp).resolve())
        # Given 发布附件，When 解析真实部署模型，Then 仅定义并发布单容器 Web 入口。
        project.validate_model()
        try:
            with https_proxy(project.dir) as (project.proxy, project.trust):
                yield project
        except BaseException:
            project.compose("logs", "--tail", "200", check_exit=False)
            raise
        finally:
            if project.compose("ps", "-a", "-q", capture=True, check_exit=False):
                # 容器内由 PG 等用户创建的目录也要能被宿主机清理。
                project.compose("stop", check_exit=False)
                project.compose("run", "--rm", "--no-deps", "--user", "0", "--entrypoint",
                                "/bin/sh", SERVICE, "-c", f"chmod -R a+rwX {DATA}",
                                check_exit=False)
            project.compose("down", check_exit=False)


class Client:
    def __init__(self, port, trust):
        self.port = port
        self.trust = trust
        self.csrf = None
        self.cookie = None
        self.access_token = None

    def request(self, method, path, *, expected=None, body=None, content_type=None):
        headers = {}
        if self.access_token:
            headers["Authorization"] = f"Bearer {self.access_token}"
        if self.cookie:
            headers["Cookie"] = self.cookie
        if self.csrf:
            headers["X-CSRF-TOKEN"] = self.csrf
        if isinstance(body, dict):
            body = json.dumps(body).encode()
            content_type = "application/json"
        if content_type:
            headers["Content-Type"] = content_type

        connection = http.client.HTTPSConnection("127.0.0.1", self.port, timeout=10, context=self.trust)
        try:
            connection.request(method, path, body, headers)
            response = connection.getresponse()
            data = response.read()
            check(expected is None or response.status == expected,
                  f"{method} {path} 预期 HTTP {expected}，实际为 {response.status}")
            return response.status, data, response.getheaders()
        finally:
            connection.close()

    def json(self, method, path, *, expected=200, body=None):
        return json.loads(self.request(method, path, expected=expected, body=body)[1])

    def csrf_request(self):
        _, body, headers = self.request("GET", "/api/auth/csrf", expected=200)
        cookies = SimpleCookie()
        for name, value in headers:
            if name.lower() == "set-cookie":
                cookies.load(value)
        cookie = cookies.get("__Host-OpenERP.Csrf")
        if cookie is not None:
            check(cookie["secure"] and cookie["httponly"] and cookie["path"] == "/",
                  "防伪 cookie 缺少 Secure、HttpOnly 或根路径属性")
            self.cookie = cookie.OutputString(attrs=[])
        check(self.cookie is not None, "首次获取防伪令牌未收到 cookie")
        self.csrf = json.loads(body)["requestToken"]

    def login(self, password):
        self.csrf_request()
        session = self.json("POST", "/api/auth/login", body={"username": "admin", "password": password})
        self.access_token = session["accessToken"]
        check(self.json("GET", "/api/auth/me")["username"] == "admin", "登录后无法读取管理员身份")


def verify_build_info(client, expected):
    # Given 未初始化实例，When 未登录匿名读取构建信息，Then 与构建时注入的三个值逐字段一致。
    check(not client.access_token and not client.cookie, "/api/about 必须在登录前匿名校验")
    _, body, _ = client.request("GET", "/api/about", expected=200)
    info = json.loads(body)
    check(isinstance(info, dict), "/api/about 未返回 JSON 对象")
    for field in BUILD_FIELDS:
        check(field in info, f"/api/about 缺少字段 {field}")
        check(info[field] == expected[field],
              f"/api/about 的 {field} 预期 {expected[field]!r}，实际 {info[field]!r}")


def verify_spa(client):
    # Given 已运行的发布容器，When 从外部直达页面、静态路径和未知 API，
    # Then 导航由 SPA 接管，静态资源和 API 的 404 保持独立。
    _, index, _ = client.request("GET", "/", expected=200)
    check(SPA_MARKER in index, "主页未返回 Vue SPA")
    for path in ("/shops", "/reports/orders", "/login", "/index.html", "/nope"):
        check(client.request("GET", path, expected=200)[1] == index, f"{path} 未返回 SPA 入口")
    for path in ("/assets/missing.js", "/missing.js", "/health"):
        client.request("GET", path, expected=404)
    client.request("POST", "/shops", expected=404)
    asset = re.search(rb'src="(/assets/[^"]+\.js)"', index)
    check(asset is not None, "SPA 未引用构建后的 JavaScript 入口")
    _, script, headers = client.request("GET", asset[1].decode(), expected=200)
    content_type = next((value for name, value in headers if name.lower() == "content-type"), "")
    check(script and SPA_MARKER not in script and "javascript" in content_type,
          "SPA JavaScript 入口未返回有效脚本")
    client.request("GET", "/api/unknown", expected=404)


def verify_persistence(client, password, file_key, shop_id):
    client.login(password)
    body = client.request("GET", f"/api/files/{file_key}?download=true", expected=200)[1]
    check(body == GIF, "重启或重建后下载文件与原文件不一致")
    check(client.json("GET", SETTINGS)["shopSids"] == [shop_id], "重启或重建后店铺同步设置未保留")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compose", required=True, type=Path, help="发布附件解包得到的 Compose 文件")
    parser.add_argument("--image", help="覆盖附件镜像地址，适用于本地和 CI")
    parser.add_argument("--upgrade-image", help="选填，验证第二个镜像复用现有数据")
    parser.add_argument("--expected-version", help="选填，构建时注入的 Release 标签；与后两项同时提供时校验 /api/about")
    parser.add_argument("--expected-commit", help="选填，构建时注入的 7 位提交")
    parser.add_argument("--expected-build-time", help="选填，构建时注入的 UTC 构建时间")
    args = parser.parse_args()
    compose_file = args.compose.resolve(strict=True)
    current_image = args.upgrade_image or args.image
    expected_build = {
        "version": args.expected_version,
        "commit": args.expected_commit,
        "buildTime": args.expected_build_time,
    }
    # 三条构建值全部提供才校验；未提供时保持原有烟测行为（如前端检查工作流）。
    if any(value is not None for value in expected_build.values()) and not all(
            value is not None for value in expected_build.values()):
        parser.error("--expected-version、--expected-commit 与 --expected-build-time 必须同时提供")
    verify_build = all(value is not None for value in expected_build.values())

    with isolated(compose_file) as primary:
        primary.configure(image=args.image)
        primary.up()
        original_id = primary.container()
        original_image = primary.inspect("{{.Image}}")
        client = primary.ready()

        # Given 全新数据目录，When 初设、登录、上传和保存业务设置，
        # Then 对外可获取身份、下载相同内容并读回已保存选店。
        check(client.json("GET", "/api/auth/status")["initialized"] is False, "新实例已初始化")
        if verify_build:
            verify_build_info(client, expected_build)
        password = secrets.token_hex(8)
        client.csrf_request()
        client.request("POST", "/api/auth/initialize", expected=204, body={"newPassword": password})
        client.login(password)
        verify_spa(client)
        upload = (
            b'--smoke\r\nContent-Disposition: form-data; name="file"; filename="smoke.gif"\r\n'
            b"Content-Type: image/gif\r\n\r\n" + GIF + b"\r\n--smoke--\r\n"
        )
        file_key = json.loads(client.request(
            "POST", "/api/files/", expected=201, body=upload,
            content_type="multipart/form-data; boundary=smoke",
        )[1])["key"]
        shop_id = 47268
        settings = client.json("PUT", SETTINGS, body={"enabled": False, "shopSids": [shop_id]})
        check(settings["shopSids"] == [shop_id], "店铺同步设置写入未返回选店")
        check(client.request("GET", f"/api/files/{file_key}?download=true", expected=200)[1] == GIF,
              "上传后下载文件与原文件不一致")
        print("通过：单容器、可信 HTTPS 反代、首次设置登录、SPA、文件读写与业务设置。", flush=True)
        print(f"初始镜像大小：{docker('image', 'inspect', '--format', '{{.Size}}', original_image)} 字节。"
              f" 容器内存：{docker('stats', '--no-stream', '--format', '{{.MemUsage}}', original_id)}。",
              flush=True)

        # Given 已使用的实例，When 正常停机后再次启动同一容器，再用现有数据重建容器，
        # Then 旧密码、文件字节和选店记录仍可读取。
        stop_seconds = primary.stop()
        primary.compose("start")
        check(primary.container() == original_id, "同版本常规启动替换了原容器")
        verify_persistence(primary.ready(), password, file_key, shop_id)
        migrations_before = primary.migration_count() if args.upgrade_image else None
        primary.configure(image=current_image)
        primary.up(recreate=not args.upgrade_image)
        check(primary.container() != original_id, "重建没有创建新容器")
        verify_persistence(primary.ready(), password, file_key, shop_id)
        new_image = primary.inspect("{{.Image}}")
        print(f"首次正常停机用时 {stop_seconds:.2f} 秒。", flush=True)
        if args.upgrade_image:
            check(new_image != original_image, "升级目标与初始镜像内容相同")
            migrations_after = primary.migration_count()
            print(f"通过：同版本启动和切换镜像后保留数据。迁移数 {migrations_before} → {migrations_after}。", flush=True)
            print(f"升级镜像大小：{docker('image', 'inspect', '--format', '{{.Size}}', new_image)} 字节。"
                  f" 容器内存：{docker('stats', '--no-stream', '--format', '{{.MemUsage}}', primary.container())}。",
                  flush=True)
        else:
            check(new_image == original_image, "同镜像重建期间镜像标签已经改变")
            print("通过：正常停机、同版本启动和同镜像重建后保留数据。跨镜像升级未验证。", flush=True)

        # Given 主实例持续运行，When 在另一个目录启动副本，
        # Then 副本有独立账户初设与数据，主实例继续可用。
        with isolated(compose_file) as second:
            second.configure(image=current_image)
            second.up()
            other = second.ready()
            check(second.proxy.upstream_port != primary.proxy.upstream_port, "两个部署共用了宿主机端口")
            check(other.json("GET", "/api/auth/status")["initialized"] is False, "副本读到了主实例账户")
            second_password = secrets.token_hex(8)
            other.csrf_request()
            other.request("POST", "/api/auth/initialize", expected=204, body={"newPassword": second_password})
            other.login(second_password)
            other.request("GET", f"/api/files/{file_key}", expected=404)
            check(other.json("GET", SETTINGS)["shopSids"] == [], "副本读到了主实例的业务设置")
            # 维护命令通过 Compose exec 直接运行，必须能读到 Postgres 配置与凭据。
            reset_password = secrets.token_hex(8)
            second.compose("exec", "-T", SERVICE, "dotnet", "/app/OpenERP.Api.dll",
                           "reset-admin-password", "--stdin", input_text=f"{reset_password}\n")
            other.request("GET", "/api/auth/me", expected=401)
            second.ready().login(reset_password)
            verify_persistence(primary.ready(), password, file_key, shop_id)
        print("通过：第二个目录独立部署，账户、文件和业务设置隔离，管理员密码可从容器内重置。", flush=True)

        # Given 已健康的实例，When API 进程意外退出，
        # Then 容器自行重启，原持久数据仍可读取。
        restarts = int(primary.inspect("{{.RestartCount}}"))
        primary.compose(
            "exec", "-T", SERVICE, "/bin/sh", "-ec",
            """uid=$(id -u openerp-api)
set --
for status in /proc/[0-9]*/status; do
    if grep -Eq "^Uid:[[:space:]]*$uid[[:space:]]" "$status" 2>/dev/null; then
        pid=${status#/proc/}
        set -- "$@" "${pid%/status}"
    fi
done
if [ "$#" != 1 ]; then
    printf '预期只有一个 API 进程，实际为 %s 个\n' "$#" >&2
    exit 1
fi
kill -KILL "$1"
""",
        )
        wait_for(lambda: int(primary.inspect("{{.RestartCount}}")) > restarts,
                 "API 进程退出后 Docker 未重启容器")
        verify_persistence(primary.ready(), password, file_key, shop_id)
        print("通过：API 进程退出后容器重启并重新提供服务。", flush=True)

        # Given 已初始化数据目录，When 整组 PostgreSQL 持久凭据缺失，
        # Then 启动失败并保留现场，禁止重新生成凭据。
        primary.stop()
        primary.compose("run", "--rm", "--no-deps", "--user", "0", "--entrypoint",
                        "/bin/sh", SERVICE, "-ec", f"mv {PG_CREDENTIALS} {PG_CREDENTIALS}.smoke")
        primary.configure(image=current_image, restart="no")
        primary.expect_failed_start("PostgreSQL 凭据缺失", "已有 PostgreSQL 数据，但缺少 PostgreSQL 凭据组")
        primary.compose("run", "--rm", "--no-deps", "--user", "0", "--entrypoint",
                        "/bin/sh", SERVICE, "-ec", f"test ! -e {PG_CREDENTIALS}")
        print("通过：已初始化实例缺少凭据时退出并保持凭据缺失状态。", flush=True)

    # Given 全新数据目录和会退出失败的迁移可执行文件，When 启动，
    # Then 实际运行迁移且阻止此容器提供 API。
    with isolated(compose_file) as migration:
        bad_bundle = migration.dir / "bad-bundle"
        bad_bundle.write_text("#!/bin/sh\necho smoke-bad-migration >&2\nexit 42\n")
        bad_bundle.chmod(0o755)
        migration.configure(image=current_image, restart="no", bad_bundle=bad_bundle)
        migration.expect_failed_start("迁移失败", "smoke-bad-migration")
    print("通过：迁移失败时容器退出且 API 没有就绪。", flush=True)


if __name__ == "__main__":
    try:
        main()
    except SmokeError as error:
        raise SystemExit(str(error))
