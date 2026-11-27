#!/usr/bin/env python3
"""用真实 Compose 解析、模拟 Docker 容器状态与故障，验证部署脚本的行为。"""

import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import unittest


UPDATE = Path(__file__).resolve().parents[1] / "houqin" / "update.sh"
DOCKER = shutil.which("docker")
OLD = "ghcr.nju.edu.cn/example/openerp/openerp:old"
NEW = "ghcr.nju.edu.cn/example/openerp/openerp:new"
OLD_ID = "sha256:" + "1" * 64
NEW_ID = "sha256:" + "2" * 64
NEW_DIGEST = "sha256:" + "3" * 64


class UpdateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        base = Path(temporary.name).resolve()
        self.instance = base / "openerp"
        self.instance.mkdir()
        self.data = self.instance / "data"
        (self.data / "postgres").mkdir(parents=True)
        self.db_file = self.data / "postgres" / "records"
        self.db_file.write_text("已有数据")
        self.db_file.chmod(0o640)
        self.env_file = self.instance / ".env"
        self.env_file.write_text("WEB_PORT=18088\nCOMPOSE_PROJECT_NAME=staging\n")
        self.old = self.instance / "docker-compose.yaml"
        self.incoming = base / "incoming.yaml"
        self.write_compose(self.old, OLD)
        self.write_compose(self.incoming, NEW)
        self.state = base / "state.json"
        self.state.write_text(json.dumps({
            "image": OLD, "image_id": OLD_ID, "id": "old-container",
            "status": "running", "started_at": "2026-11-20T00:00:00Z",
            "file": str(self.old), "port": "18088", "actions": [],
        }))
        bin_dir = base / "bin"
        bin_dir.mkdir()
        for name in ("docker", "flock"):
            wrapper = bin_dir / name
            wrapper.write_text(
                "#!/usr/bin/env python3\nimport runpy, sys\n"
                f"sys.argv.insert(1, {name!r})\nrunpy.run_path({__file__!r}, run_name='__main__')\n"
            )
            wrapper.chmod(0o755)
        self.env = os.environ | {
            "PATH": f"{bin_dir}:{os.environ['PATH']}",
            "TEST_STATE": str(self.state),
            "TEST_INSTANCE": str(self.instance),
            "TEST_DOCKER": DOCKER,
        }

    @staticmethod
    def write_compose(path, image):
        path.write_text(f"""services:
  openerp:
    image: {image}
    ports:
      - "127.0.0.1:${{WEB_PORT:-8080}}:8000"
    stop_grace_period: 120s
    volumes:
      - ./data:/var/lib/openerp
      - ./data/postgres:/var/lib/postgresql
""")

    def run_update(self, number=10, **flags):
        return subprocess.run(
            ["bash", UPDATE, self.instance, self.incoming, NEW, str(number), NEW_DIGEST],
            env=self.env | flags, text=True, capture_output=True, timeout=20,
        )

    def actual(self):
        return json.loads(self.state.read_text())

    def assert_old_instance(self):
        state = self.actual()
        self.assertEqual((state["id"], state["image"], state["status"]), ("old-container", OLD, "running"))
        self.assertIn(OLD, self.old.read_text())
        self.assertEqual(self.db_file.read_text(), "已有数据")
        self.assertEqual(self.env_file.read_text(), "WEB_PORT=18088\nCOMPOSE_PROJECT_NAME=staging\n")

    def test_updates_original_instance_with_compose_up(self):
        # Given 使用标准 Compose 文件名、非默认项目名和端口的现有实例
        original_data = self.db_file.stat()
        # When 更新至新镜像
        result = self.run_update()
        # Then 校验镜像后由 Compose 更新原实例，继续使用原数据和实例配置
        self.assertEqual(result.returncode, 0, result.stderr)
        state = self.actual()
        self.assertEqual(state["actions"], ["pull", "up"])
        self.assertEqual(state["up_args"], ["-d", "--pull", "never", "--no-build"])
        self.assertEqual((state["status"], state["image"], state["image_id"]), ("running", NEW, NEW_ID))
        self.assertEqual((state["project"], state["port"]), ("staging", "18088"))
        self.assertEqual(state["file"], str(self.old))
        self.assertEqual(self.old.read_bytes(), self.incoming.read_bytes())
        self.assertFalse((self.instance / ".houqin-backups").exists())
        self.assertEqual(self.db_file.read_text(), "已有数据")
        current_data = self.db_file.stat()
        self.assertTrue(os.path.samestat(original_data, current_data))
        self.assertEqual((current_data.st_uid, current_data.st_gid, current_data.st_mode),
                         (original_data.st_uid, original_data.st_gid, original_data.st_mode))
        self.assertEqual(self.env_file.read_text(), "WEB_PORT=18088\nCOMPOSE_PROJECT_NAME=staging\n")
        self.assertEqual((self.instance / ".houqin-run-number").read_text().strip(), "10")
        # And 部署成功后清理同仓库的旧镜像，保留本次发布，且不碰其他项目的镜像
        self.assertEqual(sorted(state["removed"]), [OLD, "ghcr.nju.edu.cn/example/openerp/api:old"])
        self.assertIn("已清理旧版本镜像 2 个", result.stdout)

    def test_rejects_compose_port_that_differs_from_existing_container(self):
        # Given 旧容器使用临时 WEB_PORT 覆盖，两个 Compose 都从 .env 解析出不同的端口
        state = self.actual()
        state["port"] = "18089"
        self.state.write_text(json.dumps(state))
        # When 自动化升级没有收到临时 WEB_PORT
        result = self.run_update()
        # Then 停机前指出如何持久化原端口，避免 HTTPS 反代失效
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("WEB_PORT=18089", result.stderr)
        self.assertEqual(self.actual()["actions"], [])
        self.assert_old_instance()

    def test_mirror_serving_another_digest_does_not_stop_original(self):
        # Given 镜像站已更新标签，但标签内容与本次发布清单不同
        # When 尝试部署已发布的清单摘要
        result = self.run_update(WRONG_DIGEST="1")
        # Then 拉取后校验失败，原容器和数据完整
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], ["pull"])
        self.assert_old_instance()
        self.assertFalse((self.instance / ".houqin-run-number").exists())

    def test_same_release_retry_uses_compose_up_and_keeps_running_container(self):
        # Given 同一发布已成功安装
        first = self.run_update()
        self.assertEqual(first.returncode, 0, first.stderr)
        installed = self.actual()
        # When 使用同一个发布序号重跑
        retry = self.run_update()
        # Then 仍执行正常部署，Compose 继续使用原容器
        self.assertEqual(retry.returncode, 0, retry.stderr)
        current = self.actual()
        self.assertEqual(current["actions"], installed["actions"] + ["pull", "up"])
        self.assertEqual(current["id"], installed["id"])
        self.assertEqual((current["status"], current["image"], current["image_id"]), ("running", NEW, NEW_ID))
        self.assertEqual(self.db_file.read_text(), "已有数据")

    def test_requires_existing_standard_compose_file(self):
        # Given 现有实例只有旧文件名的 Compose 文件
        legacy = self.old.rename(self.instance / "compose.yaml")
        self.state.write_text(json.dumps(self.actual() | {"file": str(legacy)}))
        # When 执行自动部署
        result = self.run_update()
        # Then 缺少标准 Compose 文件时停止部署，原实例继续运行
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("缺少原实例的 Compose 文件", result.stderr)
        self.assertEqual(self.actual()["actions"], [])
        self.assertIn(OLD, legacy.read_text())
        self.assertFalse(self.old.exists())

    def test_late_finishing_release_does_not_downgrade(self):
        # Given 已开始部署更新的发布序号
        (self.instance / ".houqin-run-number").write_text("11\n")
        # When 较早发布的工作流较晚到达
        result = self.run_update(number=10)
        # Then 不拉取、不停机、不触动原实例
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], [])
        self.assert_old_instance()

    def test_failure_before_switch_restores_compose_and_allows_retry(self):
        # Given 原容器持续运行，Compose 已替换文件但将在切换容器前报错
        original_compose = self.old.read_bytes()
        self.old.chmod(0o600)
        original_started = self.actual()["started_at"]
        # When 更新失败并重跑同一个发布序号
        failed = self.run_update(FAIL_BEFORE_SWITCH="unchanged")
        self.assertEqual(failed.returncode, 17, failed.stderr)
        failed_state = self.actual()
        restored_compose = self.old.read_bytes()
        restored_mode = self.old.stat().st_mode & 0o777
        leftover_temporary = list(self.instance.glob(".houqin-update.*"))
        retry = self.run_update()
        # Then 错误仍被报告，原文件可支持正常重跑，失败时容器和数据未被更改
        self.assertEqual(retry.returncode, 0, retry.stderr)
        self.assertEqual(restored_compose, original_compose)
        self.assertEqual(restored_mode, 0o600)
        self.assertFalse(leftover_temporary)
        self.assertEqual((failed_state["id"], failed_state["status"], failed_state["started_at"]),
                         ("old-container", "running", original_started))
        self.assertEqual(self.actual()["actions"], ["pull", "up", "pull", "up"])
        self.assertEqual(self.db_file.read_text(), "已有数据")
        self.assertEqual(self.old.read_bytes(), self.incoming.read_bytes())
        self.assertFalse(list(self.instance.glob(".houqin-update.*")))

    def test_failure_does_not_restore_compose_when_original_container_is_uncertain(self):
        # Given 替换文件后更新返回失败，同时原容器连续运行的条件无法确认
        for failure in ("restarted", "additional-container", "ps-error", "inspect-error"):
            with self.subTest(failure=failure):
                self.write_compose(self.old, OLD)
                self.state.write_text(json.dumps(self.actual() | {
                    "started_at": "2026-11-20T00:00:00Z", "actions": [],
                }))
                # When 失败后检查项目及容器状态
                failed = self.run_update(FAIL_BEFORE_SWITCH=failure)
                # Then 保留新版文件并退出，数据原样保留
                self.assertEqual(failed.returncode, 17, failed.stderr)
                self.assertEqual(self.old.read_bytes(), self.incoming.read_bytes())
                self.assertEqual(self.actual()["actions"], ["pull", "up"])
                self.assertEqual(self.db_file.read_text(), "已有数据")
                self.assertFalse(list(self.instance.glob(".houqin-update.*")))

    def test_hangup_before_switch_restores_compose_and_removes_temporary_files(self):
        # Given 替换配置后原容器仍持续运行
        original_compose = self.old.read_bytes()
        # When 更新进程在切换容器前收到 SIGHUP
        failed = self.run_update(FAIL_BEFORE_SWITCH="hangup")
        # Then 还原本次替换的文件，并清理临时文件
        self.assertEqual(self.old.read_bytes(), original_compose)
        self.assertEqual(self.actual()["actions"], ["pull", "up"])
        self.assertFalse(list(self.instance.glob(".houqin-update.*")))
        self.assertEqual(failed.returncode, 128 + signal.SIGHUP, failed.stderr)

    def test_start_failure_keeps_post_migration_data(self):
        # Given 新容器能写数据但 Compose 在启动过程中报错
        # When 新版更新失败
        result = self.run_update(FAIL_AFTER_START="1")
        # Then 报告失败并保留变动后的原数据目录
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], ["pull", "up"])
        self.assertEqual(self.actual()["image"], NEW)
        self.assertEqual(self.db_file.read_text(), "已经开始迁移")
        self.assertEqual(self.old.read_bytes(), self.incoming.read_bytes())
        self.assertFalse((self.instance / ".houqin-backups").exists())
        self.assertNotIn("tar --numeric-owner --same-owner -xpf", result.stderr)

    def test_rejects_compose_for_a_different_release(self):
        # Given 旧实例仍在运行，新附件引用另一版本
        self.write_compose(self.incoming, OLD)
        # When 更新目标版本
        result = self.run_update()
        # Then 拒绝更新，原实例仍在运行
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], [])
        self.assert_old_instance()


def run_fake_command():
    command, *args = sys.argv[1:]
    state_path = Path(os.environ["TEST_STATE"])
    state = json.loads(state_path.read_text())
    instance = Path(os.environ["TEST_INSTANCE"])
    failure = os.environ.get("FAIL_BEFORE_SWITCH")

    def record(action):
        state["actions"].append(action)
        state_path.write_text(json.dumps(state))

    if command == "flock":
        return 0
    if args[0] == "compose":
        if "config" in args:
            os.execv(os.environ["TEST_DOCKER"], ["docker", *args])
        file = Path(args[args.index("-f") + 1])
        position = args.index("-f") + 2
        action = args[position]
        if action == "ps":
            print(state["id"])
            if "up" in state["actions"] and failure == "additional-container":
                print("replacement-container")
            if "up" in state["actions"] and failure == "ps-error":
                return 1
            return 0
        if action == "up":
            state["up_args"] = args[position + 1:]
            if failure:
                assert NEW in (instance / file).read_text()
                if failure == "restarted":
                    state["started_at"] = "2026-11-20T00:01:00Z"
                record("up")
                if failure == "hangup":
                    os.kill(os.getppid(), signal.SIGHUP)
                return 17
            if state["image_id"] == NEW_ID and "--force-recreate" not in args:
                state["status"] = "running"
                record("up")
                return 0
            config = json.loads(subprocess.check_output(
                [os.environ["TEST_DOCKER"], *args[:position], "config", "--format", "json"],
                cwd=instance, text=True,
            ))
            state.update(status="running", id="new-container", image=NEW, image_id=NEW_ID,
                         file=str(instance / file), project=config["name"], port=config["services"]["openerp"]["ports"][0]["published"])
            record("up")
            if os.environ.get("FAIL_AFTER_START"):
                (instance / "data" / "postgres" / "records").write_text("已经开始迁移")
                return 1
            return 0
    if args[:2] == ["container", "inspect"]:
        print(json.dumps({
            "Config": {
                "Image": state["image"],
                "Labels": {
                    "com.docker.compose.project": "staging",
                    "com.docker.compose.service": "openerp",
                    "com.docker.compose.project.config_files": state["file"],
                },
            },
            "HostConfig": {"PortBindings": {"8000/tcp": [{"HostIp": "127.0.0.1", "HostPort": state["port"]}]}},
            "Mounts": [
                {"Source": str(instance / "data"), "Destination": "/var/lib/openerp", "Type": "bind", "RW": True},
                {"Source": str(instance / "data/postgres"), "Destination": "/var/lib/postgresql", "Type": "bind", "RW": True},
            ],
            "State": {"Status": state["status"], "StartedAt": state["started_at"]},
            "Image": state["image_id"],
        }))
        if "up" in state["actions"] and failure == "inspect-error":
            return 1
        return 0
    if args[:2] == ["image", "inspect"]:
        digest = OLD_ID if os.environ.get("WRONG_DIGEST") else NEW_DIGEST
        print(json.dumps({"Id": NEW_ID, "RepoDigests": [f"{NEW.rsplit(':', 1)[0]}@{digest}"]}))
        return 0
    if args[0] == "pull":
        record("pull")
        return 0
    if args[0] == "images":
        # 本机镜像：本次发布、旧版本、同仓库的其他组件，以及另一个项目的镜像
        print(NEW)
        print(OLD)
        print("ghcr.nju.edu.cn/example/openerp/api:old")
        print("ccr.example.com/other/wxw.api:v1")
        return 0
    if args[:2] == ["image", "rm"]:
        state.setdefault("removed", []).append(args[2])
        state_path.write_text(json.dumps(state))
        return 0
    raise RuntimeError(f"未知的测试命令：{command} {args}")


if __name__ == "__main__":
    if "TEST_STATE" in os.environ:
        sys.exit(run_fake_command())
    unittest.main()
