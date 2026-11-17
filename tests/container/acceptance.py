"""验收已发布的 OpenERP Compose 和已构建的 linux/amd64 测试镜像。"""

import argparse
from base64 import b64encode
from http.client import HTTPConnection, HTTPException, HTTPResponse
from http.cookies import SimpleCookie
from io import BytesIO
import json
import os
from pathlib import Path
import platform
import secrets
import subprocess
import tempfile
from threading import Event, Thread
import time


SERVICE = "openerp"
VOLUMES = {"postgres": "/var/lib/postgresql", "redis": "/var/lib/redis", "seaweedfs": "/data"}
VOLUME_TARGETS = set(VOLUMES.values())
SERVICES = ("api", "postgres", "redis", "seaweedfs")
HOST = "127.0.0.1"
STOP_TIMEOUT = 180
START_TIMEOUT = 240
HEALTH_TIMEOUT = 100
MEMORY_INTERVAL = 2
TOOL_LABEL = "org.openerp.acceptance.project"
REPOSITORY = Path(__file__).resolve().parents[2]
LATEST_MIGRATION = "20261116092521_RequireInitialPasswordChange"
MIGRATIONS_SQL = """
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" COLLATE "C";
"""


class CheckFailed(Exception):
    pass


def expect(condition, message):
    if not condition:
        raise CheckFailed(message)


class RawResponse:
    def __init__(self, data):
        self.data = data

    def makefile(self, mode):
        return BytesIO(self.data)


class Acceptance:
    def __init__(self, args, temporary):
        self.args = args
        self.project = f"openerp-acceptance-{secrets.token_hex(8)}"
        self.service_id = None
        self.can_clean_up = False
        self.steps = []
        self.metrics = {}
        self.stop_sampling = Event()
        self.sample_thread = None
        self.backup_dir = temporary / "archives"
        self.backup_dir.mkdir(mode=0o700)
        self.used_archive_tool = False
        self.cookies = {}
        self.secret_dir = temporary / "secrets"
        self.secret_dir.mkdir(mode=0o700)
        self.credentials = {
            "postgres-password": secrets.token_hex(20),
            "redis-password": secrets.token_hex(20),
            "s3-access-key": secrets.token_hex(12),
            "s3-secret-key": secrets.token_hex(20),
            "admin-initial-password": secrets.token_hex(8),
        }
        for name, value in self.credentials.items():
            path = self.secret_dir / name
            path.touch(mode=0o600)
            path.write_text(value + "\n")
        self.new_password = secrets.token_hex(8)
        self.profile_name = f"test-{secrets.token_hex(6)}"
        self.object_bytes = b"%PDF-1.4\nOpenERP storage test " + secrets.token_bytes(32)
        self.dotenv = temporary / ".env"
        self.env = os.environ.copy()
        for key in list(self.env):
            if key.startswith("COMPOSE_"):
                self.env.pop(key)
        self.env["COMPOSE_ANSI"] = "never"
        self.configure_env()

    def configure_env(self, apply_migrations=False):
        values = {
            "OPENERP_BIND_ADDRESS": HOST,
            "OPENERP_HTTP_PORT": "0",
            "OPENERP_APPLY_MIGRATIONS": "1" if apply_migrations else "0",
            "OPENERP_BINDMOUNT_0": str(self.secret_dir),
            "OPENERP_IMAGE": self.args.image,
            "ALLOWED_HOSTS": HOST,
            "TRUSTED_PROXY": HOST,
        }
        for name in values:
            self.env.pop(name, None)
        self.dotenv.write_text("".join(f"{name}={json.dumps(value)}\n" for name, value in values.items()))

    def redact(self, message):
        for value in [*self.credentials.values(), self.new_password, *self.cookies.values()]:
            if value:
                message = message.replace(value, "[redacted]")
        return message

    def docker(self, *arguments, timeout=20, data=None, combine=False):
        try:
            result = subprocess.run(
                ["docker", *arguments], input=data, stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT if combine else subprocess.PIPE,
                env=self.env, timeout=timeout,
            )
        except subprocess.TimeoutExpired:
            raise CheckFailed(f"Docker 命令超过 {timeout} 秒。") from None
        if result.returncode:
            diagnostic = self.redact((result.stderr or result.stdout).decode(errors="replace").strip())
            raise CheckFailed(f"Docker 命令退出码为 {result.returncode}。{diagnostic}")
        return result.stdout

    def compose(self, *arguments, timeout=30):
        return self.docker(
            "compose", "--project-name", self.project, "--env-file", str(self.dotenv),
            "-f", str(self.args.compose), *arguments, timeout=timeout,
        )

    def step(self, name, action):
        print(f"[acceptance] {name}", flush=True)
        started = time.monotonic()
        record = {"name": name, "status": "failed"}
        self.steps.append(record)
        try:
            result = action()
            record["status"] = "passed"
            return result
        finally:
            record["durationSeconds"] = round(time.monotonic() - started, 2)

    def preflight(self):
        image, = json.loads(self.docker("image", "inspect", self.args.image))
        expect((image["Os"], image["Architecture"]) == ("linux", "amd64"),
               "验收镜像必须是 linux/amd64。")
        self.image_id = image["Id"]
        daemon = self.docker("info", "--format", "{{.OSType}}/{{.Architecture}}").decode().strip()
        self.metrics.update(
            imagePlatform="linux/amd64",
            runnerPlatform=f"{platform.system()}/{platform.machine()}",
            daemonPlatform=daemon,
            imageSizeBytes=image["Size"],
            memorySamplingIntervalSeconds=MEMORY_INTERVAL,
            memorySamples=0,
        )

        config = json.loads(self.compose("config", "--format", "json"))
        expect(set(config["services"]) == {SERVICE}, "生成的 Compose 必须仅包含 openerp。")
        service = config["services"][SERVICE]
        mounts = {mount["target"]: mount for mount in service["volumes"]}
        expect(set(mounts) == VOLUME_TARGETS | {"/run/secrets"} and len(service["volumes"]) == 4,
               "生成的 Compose 必须包含三个数据卷和一个密钥挂载。")
        secret = mounts["/run/secrets"]
        expect(secret["type"] == "bind" and secret.get("read_only") is True,
               "生成的 /run/secrets 必须是只读 bind mount。")
        expect(Path(secret["source"]).resolve() == self.secret_dir.resolve(),
               "密钥目录不属于本次验收。")
        volumes = config.get("volumes", {})
        expect(all(mounts[target]["type"] == "volume" for target in VOLUME_TARGETS),
               "持久化路径必须使用 Compose 命名卷。")
        expect({mounts[target]["source"] for target in VOLUME_TARGETS} == set(volumes)
               and len(volumes) == 3, "三个持久化路径必须使用各自的命名卷。")
        for volume in volumes.values():
            expect(not volume.get("external") and not volume.get("driver_opts")
                   and volume.get("driver", "local") == "local"
                   and volume["name"].startswith(self.project + "_"),
                   "验收只允许本次 Compose 项目的本地卷。")
        self.volume_keys = {key: mounts[target]["source"] for key, target in VOLUMES.items()}
        self.volume_names = {key: volumes[source]["name"] for key, source in self.volume_keys.items()}
        networks = config.get("networks", {})
        expect(len(networks) == 1 and not service.get("network_mode"),
               "验收仅支持一个项目网络。")
        expect(not service.get("container_name") and not service.get("volumes_from"),
               "验收需要由 Compose 创建项目内的容器和卷。")
        expect(all(not net.get("external") and not net.get("driver_opts")
                   and net.get("driver", "bridge") == "bridge"
                   and net["name"].startswith(self.project + "_") for net in networks.values()),
               "验收只允许本次 Compose 项目的桥接网络。")
        expect(not config.get("secrets") and not config.get("configs"),
               "验收只使用独立的密钥目录。")
        ports = service.get("ports", [])
        expect(len(ports) == 1 and ports[0]["target"] == 8081 and ports[0].get("host_ip") == HOST
               and str(ports[0].get("published")) == "0",
               "生成的 Compose 必须只将 API 发布到回环地址上的临时端口。")
        expect(service["image"] == self.args.image,
               "验收使用的 Compose 镜像与指定镜像不匹配。")
        expect(not self.compose("ps", "-a", "-q").strip(), "验收 Compose 项目已经存在容器。")
        existing = self.docker("volume", "ls", "-q", "--filter", f"name={self.project}")
        expect(not existing.strip(), "验收 Compose 项目已经存在卷。")
        self.can_clean_up = True

    def inspect(self, *, size=False):
        flags = ["--size"] if size else []
        raw = self.docker("inspect", "--type", "container", *flags, self.service_id, timeout=15 if size else 8)
        container, = json.loads(raw)
        expect(container["Config"]["Labels"].get("com.docker.compose.project") == self.project,
               "容器与验收项目不匹配。")
        expect(container["Image"] == self.image_id, "业务容器镜像与验收时检查的镜像不匹配。")
        return container

    def up(self, recreate=False, no_start=False):
        flags = ["--no-build", "--pull", "never"]
        flags += ["--no-start"] if no_start else ["-d"]
        if recreate:
            flags += ["--force-recreate"]
        self.compose("up", *flags, SERVICE, timeout=STOP_TIMEOUT + 30)
        self.service_id = self.compose("ps", "-a", "-q", SERVICE).decode().strip()
        expect(self.service_id and len(self.service_id.splitlines()) == 1, "未找到唯一的 OpenERP 容器。")
        self.inspect()
        self.docker("update", "--restart=no", self.service_id)

    def sample_memory(self):
        peak = 0
        samples = 0
        units = {"B": 1, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "TiB": 1024**4}
        while not self.stop_sampling.is_set():
            container_id = self.service_id
            if container_id:
                try:
                    usage = self.docker("stats", "--no-stream", "--format", "{{.MemUsage}}",
                                        container_id, timeout=5).decode().partition("/")[0].strip()
                    for unit in sorted(units, key=len, reverse=True):
                        if usage.endswith(unit):
                            size = round(float(usage.removesuffix(unit)) * units[unit])
                            if size > 0:
                                samples += 1
                                peak = max(peak, size)
                            break
                except (CheckFailed, OSError, ValueError):
                    # 容器停止或被替换的间隙可能没有运行数据。
                    pass
            self.stop_sampling.wait(MEMORY_INTERVAL)
        self.metrics["memorySamples"] = samples
        if samples:
            self.metrics["peakSampledMemoryBytes"] = peak
            self.metrics["memoryMeasurement"] = "Docker stats 的离散采样；峰值受采样时机和 CLI 精度限制"

    def execute(self, *arguments, data=None, combine=False):
        return self.docker("exec", "-i", self.service_id, *arguments, data=data, combine=combine)

    def wait(self, *, health=None, exit_code=None, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            container = self.inspect()
            state = container["State"]
            if not state["Running"]:
                if exit_code == "nonzero" and state["ExitCode"] != 0:
                    return container
                if exit_code == 0 and state["ExitCode"] == 0:
                    return container
                raise CheckFailed(f"容器已退出，退出码 {state['ExitCode']}，预期 {health or exit_code}。")
            if health and state.get("Health", {}).get("Status") == health:
                return container
            time.sleep(1)
        raise CheckFailed(f"等待容器达到 {health or exit_code} 超过 {timeout} 秒。")

    def healthy(self):
        return self.wait(health="healthy", timeout=START_TIMEOUT)

    def sql(self, statement):
        return self.execute(
            "/bin/sh", "-ec",
            'export PGPASSWORD="$(cat /run/openerp/postgres-password)"; '
            'exec psql -wXqAt -h 127.0.0.1 -U openerp -d openerp -v ON_ERROR_STOP=1',
            data=statement.encode(),
        ).decode().strip()

    def redis(self, *arguments):
        return self.execute(
            "/bin/sh", "-ec",
            'export REDISCLI_AUTH="$(cat /run/openerp/redis-password)"; '
            'exec redis-cli -e --raw -h 127.0.0.1 "$@"',
            "redis-fixture", *arguments,
        ).decode().strip()

    def migration_ids(self):
        migrations = (REPOSITORY / "OpenERP.Api/Migrations").glob("??????????????_*.cs")
        return sorted(path.stem for path in migrations if not path.name.endswith(".Designer.cs"))

    def first_start(self):
        self.up()
        container = self.healthy()
        expect(container["Path"] == "/init" and self.execute("cat", "/proc/1/comm").strip() == b"s6-svscan",
               "OpenERP 容器没有由 s6 管理 PID 1。")
        expect(self.sql(MIGRATIONS_SQL).splitlines() == self.migration_ids(), "初始化后迁移历史不完整。")
        expect(self.sql('SELECT "MustChangePassword" FROM "AdminAccounts" WHERE "Id" = 1;') == "t",
               "管理员未初始化为首次改密状态。")
        published = container["NetworkSettings"]["Ports"]
        expect({port for port, binding in published.items() if binding} == {"8081/tcp"},
               "容器发布了 API 以外的端口。")

    def anonymous_routes(self):
        binding, = self.inspect()["NetworkSettings"]["Ports"]["8081/tcp"]
        expect(binding["HostIp"] == HOST, "API 发布地址不是回环地址。")
        connection = HTTPConnection(HOST, int(binding["HostPort"]), timeout=12)
        try:
            connection.request("GET", "/login", headers={"Host": HOST})
            response = connection.getresponse()
            expect(response.status == 200 and response.headers.get_content_type() == "text/html"
                   and b"<html" in response.read().lower(), "/login 未匿名返回 SPA HTML。")
            connection.request("GET", "/api/unknown", headers={"Host": HOST})
            response = connection.getresponse()
            expect(response.status in (401, 404) and response.headers.get_content_type() != "text/html"
                   and b"<html" not in response.read().lower(), "未知 API 路径回退到了 SPA。")
        finally:
            connection.close()

    def http(self, method, path, status, *, data=None, content_type=None, csrf=None):
        headers = {"Host": HOST, "X-Forwarded-Proto": "https"}
        if self.cookies:
            headers["Cookie"] = "; ".join(f"{name}={value}" for name, value in self.cookies.items())
        if data is not None:
            headers["Content-Type"] = content_type or "application/json"
        if csrf:
            headers["X-CSRF-TOKEN"] = csrf

        # 经过可信回环代理的请求头让服务器执行正常的 Secure cookie 与 CSRF 校验。
        # 此处传输是容器内 HTTP，校验范围不包括代理 TLS。
        config = "".join(f"header = {json.dumps(f'{name}: {value}')}\n" for name, value in headers.items())
        # 密码、会话和 CSRF 经 stdin/容器临时文件传递，不进入宿主 Docker 命令行。
        raw = self.execute(
            "/bin/sh", "-ec",
            'directory=$(mktemp -d); trap \'rm -rf "$directory"\' EXIT; '
            'IFS= read -r config; printf "%s" "$config" | base64 -d > "$directory/curl.conf"; '
            'method=$1; url=$2; '
            'if [ "$3" = yes ]; then '
            'IFS= read -r body; printf "%s" "$body" | base64 -d > "$directory/body"; '
            'set -- --data-binary "@$directory/body"; else set --; fi; '
            'curl --silent --show-error --include --raw --http1.1 --max-time 12 '
            '--config "$directory/curl.conf" --request "$method" "$url" "$@"',
            "http-fixture", method, f"http://127.0.0.1:8081{path}", "yes" if data is not None else "no",
            data=b64encode(config.encode()) + b"\n" + b64encode(data or b"") + b"\n",
        )
        response = HTTPResponse(RawResponse(raw))
        response.begin()
        body = response.read()
        expect(response.status == status, f"{method} {path} 预期 HTTP {status}，实际 {response.status}。")
        for value in response.headers.get_all("Set-Cookie", []):
            for name, cookie in SimpleCookie(value).items():
                expect(cookie["secure"], "认证 cookie 缺少 Secure 属性。")
                self.cookies[name] = cookie.value
        return body

    def csrf(self):
        return json.loads(self.http("GET", "/api/auth/csrf", 200))["requestToken"]

    def login(self, password):
        self.cookies.clear()
        body = json.dumps({"username": "admin", "password": password}).encode()
        return json.loads(self.http("POST", "/api/auth/login", 200, data=body, csrf=self.csrf()))

    def store_data(self):
        initial_password = self.credentials["admin-initial-password"]
        expect(self.login(initial_password)["mustChangePassword"], "首次登录未要求改密。")
        passwords = {"oldPassword": initial_password, "newPassword": self.new_password}
        self.http("POST", "/api/auth/change-password", 204, data=json.dumps(passwords).encode(), csrf=self.csrf())
        expect(not self.login(self.new_password)["mustChangePassword"], "改密后的会话仍要求改密。")

        profile = json.dumps({"name": self.profile_name}).encode()
        self.http("PUT", "/api/account/profile", 200, data=profile, csrf=self.csrf())
        expect(self.sql('SELECT "Name" FROM "AdminAccounts" WHERE "Id" = 1;') == self.profile_name,
               "管理员资料未写入 PostgreSQL。")
        unauthenticated = self.execute("redis-cli", "--raw", "GET", self.project, combine=True)
        expect(b"NOAUTH" in unauthenticated, "Redis 未拒绝未经认证的读取。")
        expect(self.redis("SET", self.project, self.profile_name) == "OK", "Redis 认证写入失败。")

        boundary = secrets.token_hex(16)
        data = (
            f"--{boundary}\r\n"
            'Content-Disposition: form-data; name="file"; filename="acceptance.pdf"\r\n'
            "Content-Type: application/pdf\r\n\r\n"
        ).encode() + self.object_bytes + f"\r\n--{boundary}--\r\n".encode()
        uploaded = self.http("POST", "/api/files/", 201, data=data, csrf=self.csrf(),
                             content_type=f"multipart/form-data; boundary={boundary}")
        self.object_path = f"/api/files/{json.loads(uploaded)['key']}"
        expect(self.http("GET", self.object_path, 200) == self.object_bytes, "读回的对象内容不一致。")

    def production_sync_routes(self):
        config = json.loads(self.http("GET", "/api/erp/config", 200))
        expect(config["configured"] is False and config["orderSyncEnabled"] is True,
               "Production 未注册同步能力或错误地注入了 ERP 凭证。")
        self.http("POST", "/api/erp/shops/30/order-sync", 503, csrf=self.csrf())
        settings_path = "/api/erp/order-sync-settings"
        disabled = {"enabled": False, "shopSids": [30]}
        self.http("PUT", settings_path, 200, data=json.dumps(disabled).encode(), csrf=self.csrf())
        actual = json.loads(self.http("GET", settings_path, 200))
        expect(actual["enabled"] is False and actual["shopSids"] == [30],
               "Production 定时同步设置未被保存。")
        self.http("PUT", settings_path, 503,
                  data=json.dumps({"enabled": True, "shopSids": [30]}).encode(), csrf=self.csrf())
        self.http("PUT", settings_path, 200,
                  data=json.dumps({"enabled": False, "shopSids": []}).encode(), csrf=self.csrf())

    def replace_container(self):
        old_id = self.service_id
        self.compose("stop", "--timeout", str(STOP_TIMEOUT), SERVICE, timeout=STOP_TIMEOUT + 15)
        stopped = self.wait(exit_code=0, timeout=15)
        logs = self.logs_since(stopped["State"]["StartedAt"])
        api_stop = logs.find("service api successfully stopped")
        expect(api_stop >= 0 and all(logs.find(f"service {name}: stopping") > api_stop for name in SERVICES[1:]),
               "没有观察到 API 先停止且基础设施随后关闭。")
        self.up(recreate=True)
        self.healthy()
        expect(self.service_id != old_id, "容器未被替换。")
        self.verify_persistent_data("替换容器")

    def verify_persistent_data(self, operation):
        expect(json.loads(self.http("GET", "/api/auth/me", 200))["name"] == self.profile_name,
               f"{operation}后管理员资料或会话没有保留。")
        expect(self.redis("GET", self.project) == self.profile_name, f"{operation}后 Redis 数据没有保留。")
        expect(self.http("GET", self.object_path, 200) == self.object_bytes, f"{operation}后对象内容没有保留。")

    def checked_volume_names(self, state):
        container = self.inspect()
        expect(not container["State"]["Running"] and container["State"]["Status"] == state,
               "归档操作要求业务容器已停止或尚未启动。")
        volumes = {mount["Destination"]: mount["Name"] for mount in container["Mounts"]
                   if mount["Type"] == "volume"}
        expect(volumes == {target: self.volume_names[key] for key, target in VOLUMES.items()},
               "实际业务容器卷与验收项目卷不匹配。")
        self.check_volume_ownership(list(self.volume_names.values()))
        return self.volume_names

    def check_volume_ownership(self, names):
        keys_by_name = {name: key for key, name in self.volume_names.items()}
        expect(set(names) <= set(keys_by_name), "拒绝操作验收项目未声明的卷。")
        details = json.loads(self.docker("volume", "inspect", *names))
        expect({volume["Name"] for volume in details} == set(names),
               "未找到本次验收的完整卷集合。")
        for volume in details:
            key = keys_by_name[volume["Name"]]
            labels = volume.get("Labels") or {}
            expect(volume["Driver"] == "local"
                   and labels.get("com.docker.compose.project") == self.project
                   and labels.get("com.docker.compose.volume") == self.volume_keys[key],
                   "拒绝操作不属于本次验收项目的卷。")

    def archive_volumes(self, *, restore=False):
        names = self.checked_volume_names("created" if restore else "exited")
        mounts = []
        operations = ["umask 077"]
        for key, path in VOLUMES.items():
            source = names[key]
            mount = f"type=volume,source={source},target={path},volume-nocopy"
            mounts += ["--mount", mount if restore else mount + ",readonly"]
            if restore:
                # Compose 创建新卷时可能复制镜像中的空目录；禁止覆盖任何已存在的数据文件。
                operations += [f'test -z "$(find {path} -type f -print -quit)"',
                               f'tar -xpf /archives/{key}.tar -C {path}']
            else:
                operations += [f'tar -cf /archives/{key}.tar -C {path} .',
                               f'printf "{key} "; du -sk {path} | cut -f1']
        archive_mount = f"type=bind,source={self.backup_dir},target=/archives"
        self.used_archive_tool = True
        result = self.docker(
            "run", "--rm", "--name", f"{self.project}-archives", "--network", "none",
            "--pull", "never", "--platform", "linux/amd64", "--user", "0:0", "--entrypoint", "/bin/sh",
            "--label", f"{TOOL_LABEL}={self.project}",
            "--mount", archive_mount + ",readonly" if restore else archive_mount,
            *mounts, self.image_id, "-ec", "\n".join(operations), timeout=120,
        )
        if not restore:
            return {key: int(kib) * 1024 for key, kib in map(str.split, result.decode().splitlines())}

    def backup_and_restore(self):
        container = self.inspect(size=True)
        self.metrics["containerWritableLayerBytes"] = container["SizeRw"]
        self.metrics["containerRootFilesystemBytes"] = container["SizeRootFs"]
        self.compose("stop", "--timeout", str(STOP_TIMEOUT), SERVICE, timeout=STOP_TIMEOUT + 15)
        self.wait(exit_code=0, timeout=15)

        disk_bytes = self.archive_volumes()
        expect(set(disk_bytes) == set(VOLUMES) and all(size > 0 for size in disk_bytes.values()),
               "未测到三个数据卷的磁盘使用量。")
        archives = {key: (self.backup_dir / f"{key}.tar").stat().st_size for key in VOLUMES}
        expect(all(size > 0 for size in archives.values()), "数据库、缓存或对象卷归档为空。")
        self.metrics["volumeAllocatedBytes"] = disk_bytes
        self.metrics["backupArchiveBytes"] = archives

        self.remove_project()
        remaining = self.docker("volume", "ls", "-q", "--filter", f"label=com.docker.compose.project={self.project}")
        expect(not remaining.strip(), "验收项目卷未全部删除，无法验证空卷恢复。")
        self.service_id = None
        self.up(no_start=True)
        self.archive_volumes(restore=True)
        self.docker("start", self.service_id)
        self.healthy()
        self.verify_persistent_data("恢复备份")
        self.http("DELETE", self.object_path, 204, csrf=self.csrf())
        self.http("GET", self.object_path, 404)

    def crash_service(self, service):
        self.execute("/command/s6-svc", "-k", f"/run/service/{service}")
        self.wait(exit_code="nonzero", timeout=STOP_TIMEOUT + 15)
        self.docker("start", self.service_id)
        self.healthy()

    def unhealthy_dependency(self):
        before = self.inspect()
        self.execute("/command/s6-svc", "-p", "/run/service/redis")
        try:
            unhealthy = self.wait(health="unhealthy", timeout=HEALTH_TIMEOUT)
            expect(unhealthy["RestartCount"] == before["RestartCount"]
                   and unhealthy["State"]["StartedAt"] == before["State"]["StartedAt"],
                   "依赖进程存活但不可用时，容器不应重新启动。")
        finally:
            self.execute("/command/s6-svc", "-c", "/run/service/redis")
        self.wait(health="healthy", timeout=HEALTH_TIMEOUT)

    def expect_schema_refusal(self, diagnostic):
        stopped = self.wait(exit_code="nonzero", timeout=START_TIMEOUT)
        expect(diagnostic in self.logs_since(stopped["State"]["StartedAt"]),
               "容器退出，但没有收到预期的数据库门控日志。")

    def logs_since(self, started_at):
        return self.docker("logs", "--since", started_at, self.service_id, combine=True).decode(errors="replace")

    def check_pending_migration(self):
        # 反向执行当前最后一个迁移的 Down，并移除对应历史，构造仍合法的前一个版本。
        expect(self.migration_ids()[-1] == LATEST_MIGRATION, "旧数据库 fixture 需要随迁移变更更新。")
        self.sql(f"""
BEGIN;
ALTER TABLE "AdminAccounts" DROP COLUMN "MustChangePassword";
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{LATEST_MIGRATION}';
COMMIT;
""")
        self.docker("restart", "-t", str(STOP_TIMEOUT), self.service_id, timeout=STOP_TIMEOUT + 20)
        self.expect_schema_refusal("Migrations pending")
        self.configure_env(apply_migrations=True)
        self.up(recreate=True)
        self.healthy()
        expect(self.sql(MIGRATIONS_SQL).splitlines() == self.migration_ids(), "授权升级后的迁移历史不完整。")
        expect(self.sql('SELECT "Name" FROM "AdminAccounts" WHERE "Id" = 1;') == self.profile_name,
               "授权升级后已有应用数据被更改。")

    def check_unknown_migration(self):
        self.sql("""
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20990101000000_AcceptanceUnknown', '10.0.0');
""")
        self.docker("restart", "-t", str(STOP_TIMEOUT), self.service_id, timeout=STOP_TIMEOUT + 20)
        self.expect_schema_refusal("history is incompatible")

    def run(self):
        self.step("生成的 Compose 与 linux/amd64 镜像", self.preflight)
        self.sample_thread = Thread(target=self.sample_memory, daemon=True)
        self.sample_thread.start()
        self.step("初始化并检查健康、迁移、管理员与 PID 1", self.first_start)
        self.step("匿名 SPA 与未知 API 路由", self.anonymous_routes)
        self.step("管理员首次改密、数据库写入、Redis 认证与对象存取", self.store_data)
        self.step("Production 手动同步与定时设置入口", self.production_sync_routes)
        self.step("正常停止、替换容器并检查三个持久卷", self.replace_container)
        self.step("离线备份三个数据卷并恢复到新建的验收卷", self.backup_and_restore)
        self.step("Redis 暂停导致 unhealthy 且能恢复", self.unhealthy_dependency)
        for service in SERVICES:
            self.step(f"{service} 意外退出时容器失败并可再次启动", lambda: self.crash_service(service))
        self.step("已知旧数据库需显式授权迁移", self.check_pending_migration)
        self.step("无法识别的迁移历史拒绝启动", self.check_unknown_migration)

    def remove_project(self):
        if self.can_clean_up:
            self.compose("down", "--timeout", str(STOP_TIMEOUT), timeout=STOP_TIMEOUT + 25)
            names = self.docker(
                "volume", "ls", "-q", "--filter", f"label=com.docker.compose.project={self.project}",
            ).decode().split()
            if names:
                self.check_volume_ownership(names)
                self.docker("volume", "rm", *names)

    def finish_sampling(self):
        self.stop_sampling.set()
        if self.sample_thread:
            self.sample_thread.join(timeout=8)

    def clean_up(self):
        if self.used_archive_tool:
            tools = self.docker("ps", "-aq", "--filter", f"label={TOOL_LABEL}={self.project}").decode().split()
            if tools:
                self.docker("rm", "--force", *tools)
        self.remove_project()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compose", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    if not args.compose.is_absolute() or not args.compose.is_file():
        parser.error("--compose 必须是已有 Compose 文件的绝对路径")

    report = {"image": args.image, "transport": "HTTP，认证请求模拟可信代理头；不验证 TLS"}
    with tempfile.TemporaryDirectory(prefix="openerp-acceptance-", dir="/tmp") as directory:
        acceptance = Acceptance(args, Path(directory))
        report["project"] = acceptance.project
        report["steps"] = acceptance.steps
        report["metrics"] = acceptance.metrics
        finished = False
        try:
            acceptance.run()
            finished = True
        except (CheckFailed, HTTPException, OSError, ValueError, KeyError) as error:
            report["error"] = acceptance.redact(str(error))
            print(f"[acceptance] {report['error']}", flush=True)
            if acceptance.service_id:
                try:
                    report["failureLogs"] = acceptance.redact(acceptance.docker(
                        "logs", "--tail", "150", acceptance.service_id, combine=True,
                    ).decode(errors="replace"))
                except (CheckFailed, OSError):
                    report["failureLogs"] = "无法取得容器日志。"
        finally:
            acceptance.finish_sampling()
            if finished and not acceptance.metrics["memorySamples"]:
                report["error"] = "未采到业务容器内存数据。"
                finished = False
            try:
                acceptance.clean_up()
            except (CheckFailed, OSError) as error:
                report["cleanupError"] = acceptance.redact(str(error))
            report["passed"] = finished and "cleanupError" not in report
            args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
