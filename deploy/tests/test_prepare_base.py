import base64
import hashlib
import http.server
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

from deploy.prepare_base import prepare_base


class Registry(http.server.BaseHTTPRequestHandler):
    def send_json(self, status, body, **headers):
        self.send_response(status)
        for key, value in headers.items():
            self.send_header(key, value)
        self.end_headers()
        self.wfile.write(json.dumps(body).encode())

    def do_GET(self):
        path = urlsplit(self.path)
        if path.path == "/token":
            self.server.token_scopes.append(parse_qs(path.query).get("scope"))
            if self.headers.get("Authorization") != "Basic " + base64.b64encode(b"actor:secret").decode():
                self.send_json(401, {"errors": [{"code": "UNAUTHORIZED"}]})
            elif self.server.token_status == 200:
                self.send_json(200, {"token": "registry-token"})
            else:
                self.send_json(self.server.token_status, {"errors": [{"code": "DENIED"}]})
        elif self.headers.get("Authorization") != "Bearer registry-token":
            self.send_json(401, {"errors": [{"code": "UNAUTHORIZED"}]})
        elif path.path != self.server.manifest_path:
            self.send_json(404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]})
        else:
            self.server.manifest_reads += 1
            status, body, headers = self.server.manifest
            if status is None:
                return
            self.send_json(status, body, **headers)

    def log_message(self, *args):
        pass


class PrepareBaseTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.dockerfile = Path(folder.name) / "base.Dockerfile"
        self.dockerfile.write_bytes(b"FROM scratch\n")
        self.repository = "example/openerp/openerp-base"
        self.tag = "linux-amd64-" + hashlib.sha256(b"FROM scratch\n").hexdigest()
        self.digest = "sha256:" + "0" * 64
        self.present = (200, {}, {"Docker-Content-Digest": self.digest})
        self.server = http.server.HTTPServer(("127.0.0.1", 0), Registry)
        self.registry = f"http://127.0.0.1:{self.server.server_port}"
        self.image = f"127.0.0.1:{self.server.server_port}/{self.repository}"
        self.server.token_scopes = []
        self.server.token_status = 200
        self.server.manifest_path = f"/v2/{self.repository}/manifests/{self.tag}"
        self.server.manifest_reads = 0
        self.server.manifest = self.present
        thread = threading.Thread(target=self.server.serve_forever)
        thread.start()
        self.addCleanup(thread.join)
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)

    def prepare(self):
        return prepare_base(self.registry, self.repository, "actor", "secret", self.dockerfile)

    def test_reuses_the_authenticated_manifest_by_digest(self):
        # 已有对应 Dockerfile 的 manifest 时，复用其不可变引用。
        with patch("subprocess.run") as docker:
            self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
        docker.assert_not_called()
        self.assertEqual(self.server.token_scopes, [[f"repository:{self.repository}:pull,push"]])
        self.assertEqual(self.server.manifest_reads, 1)

    def test_pushes_once_when_the_repository_or_tag_is_missing(self):
        # 已认证的仓库或标签不存在时，发布一次并取得推送后的摘要。
        for code in ("NAME_UNKNOWN", "MANIFEST_UNKNOWN"):
            with self.subTest(code=code):
                self.server.manifest_reads = 0
                self.server.manifest = (404, {"errors": [{"code": code}]}, {})

                def publish(*args, **kwargs):
                    self.server.manifest = self.present

                with patch("subprocess.run", side_effect=publish) as docker:
                    self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
                docker.assert_called_once()
                command = docker.call_args.args[0]
                self.assertIn("--push", command)
                self.assertEqual(command[command.index("--platform") + 1], "linux/amd64")
                self.assertEqual(command[command.index("--tag") + 1], f"{self.image}:{self.tag}")
                self.assertEqual(command[-1], "-")
                self.assertNotIn("--file", command)
                self.assertEqual(docker.call_args.kwargs["input"], b"FROM scratch\n")
                self.assertEqual(self.server.manifest_reads, 2)

    def test_retries_reuse_a_published_recipe_and_only_changed_recipe_pushes_again(self):
        # 首次发布、重试和修改 Dockerfile 后发布，分别验证每个标签只推送一次。
        missing = (404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]}, {})
        self.server.manifest = missing
        (self.dockerfile.parent / "unused-context-file").write_text("不应传给构建器")

        def publish(*args, **kwargs):
            self.server.manifest = self.present

        with patch("subprocess.run", side_effect=publish) as docker:
            self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
            self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
            self.assertEqual(docker.call_count, 1)

            changed = b"FROM scratch\nENV VERSION=2\n"
            self.dockerfile.write_bytes(changed)
            changed_tag = "linux-amd64-" + hashlib.sha256(changed).hexdigest()
            self.server.manifest_path = f"/v2/{self.repository}/manifests/{changed_tag}"
            self.server.manifest = missing
            self.digest = "sha256:" + "1" * 64
            self.present = (200, {}, {"Docker-Content-Digest": self.digest})
            self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
            self.assertEqual(self.prepare(), f"{self.image}@{self.digest}")
            self.assertEqual(docker.call_count, 2)

        for call, recipe in zip(docker.call_args_list, (b"FROM scratch\n", changed)):
            command = call.args[0]
            tag = "linux-amd64-" + hashlib.sha256(recipe).hexdigest()
            self.assertEqual(command[command.index("--tag") + 1], f"{self.image}:{tag}")
            self.assertEqual(command[-1], "-")
            self.assertEqual(call.kwargs["input"], recipe)

    def test_rejects_token_failures_without_building(self):
        # 令牌交换失败时，中止且不推送镜像。
        for status in (401, 403, 404, 429, 500):
            with self.subTest(status=status):
                self.server.token_status = status
                with patch("subprocess.run") as docker, self.assertRaisesRegex(RuntimeError, "令牌"):
                    self.prepare()
                docker.assert_not_called()

    def test_rejects_manifest_failures_without_building(self):
        # 无法确认镜像是否缺失时，中止且不推送镜像。
        for status, code in ((401, "UNAUTHORIZED"), (403, "DENIED"), (404, "DENIED"),
                             (404, None), (429, "TOOMANYREQUESTS"), (500, "UNKNOWN"), (None, None)):
            with self.subTest(status=status, code=code):
                self.server.manifest = (status, {"errors": [{"code": code}]} if code else {}, {})
                with patch("subprocess.run") as docker, self.assertRaisesRegex(RuntimeError, "manifest"):
                    self.prepare()
                docker.assert_not_called()

    def test_requires_a_valid_digest_on_success(self):
        # manifest 响应缺少有效摘要时，中止且不推送镜像。
        for digest in (None, "latest", "sha256:abcdef"):
            with self.subTest(digest=digest):
                self.server.manifest = (200, {}, {"Docker-Content-Digest": digest} if digest else {})
                with patch("subprocess.run") as docker, self.assertRaisesRegex(RuntimeError, "摘要"):
                    self.prepare()
                docker.assert_not_called()

    def test_requires_a_manifest_after_push(self):
        # 推送后标签仍缺失时，不向下游输出镜像引用。
        self.server.manifest = (404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]}, {})
        with patch("subprocess.run") as docker, self.assertRaisesRegex(RuntimeError, "推送"):
            self.prepare()
        docker.assert_called_once()
