#!/usr/bin/env python3
"""用真实 Compose 解析、模拟 Docker 容器状态与故障，验证部署脚本的文件和恢复行为。"""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
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
        self.old = self.instance / "compose.yaml"
        self.incoming = base / "incoming.yaml"
        self.write_compose(self.old, OLD)
        self.write_compose(self.incoming, NEW)
        self.state = base / "state.json"
        self.state.write_text(json.dumps({
            "image": OLD, "image_id": OLD_ID, "id": "old-container",
            "status": "running", "exit": 0, "file": str(self.old), "port": "18088", "actions": [],
        }))
        bin_dir = base / "bin"
        bin_dir.mkdir()
        for name in ("docker", "flock", "tar"):
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
            "TEST_TAR": shutil.which("tar"),
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

    def test_updates_original_instance_after_backing_up_legacy_compose(self):
        # Given 使用旧 Compose 文件名、非默认项目名和端口的现有实例
        # When 更新至新镜像
        result = self.run_update()
        # Then 拉取后才停机，备份可成套恢复，原数据和实例配置继续使用
        self.assertEqual(result.returncode, 0, result.stderr)
        state = self.actual()
        self.assertEqual(state["actions"], ["pull", "stop", "archive", "up"])
        self.assertEqual((state["status"], state["image"], state["image_id"]), ("running", NEW, NEW_ID))
        self.assertEqual((state["project"], state["port"]), ("staging", "18088"))
        self.assertFalse(self.old.exists())
        self.assertEqual((self.instance / "docker-compose.yaml").read_bytes(), self.incoming.read_bytes())
        [backup] = self.instance.glob(".houqin-backups/*/pre-update.tar")
        self.assertEqual(backup.parent.stat().st_mode & 0o777, 0o700)
        with tarfile.open(backup) as archive:
            self.assertIn(OLD, archive.extractfile("compose.yaml").read().decode())
            self.assertEqual(archive.extractfile(".env").read(), self.env_file.read_bytes())
            member = archive.getmember("data/postgres/records")
            self.assertEqual(archive.extractfile(member).read().decode(), "已有数据")
            self.assertEqual((member.uid, member.gid, member.mode),
                             (self.db_file.stat().st_uid, self.db_file.stat().st_gid, 0o640))
        self.assertEqual(self.db_file.read_text(), "已有数据")
        self.assertEqual(self.env_file.read_text(), "WEB_PORT=18088\nCOMPOSE_PROJECT_NAME=staging\n")
        self.assertEqual((self.instance / ".houqin-run-number").read_text().strip(), "10")

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

    def test_same_release_retry_keeps_running_container_and_original_backup(self):
        # Given 同一发布已成功安装
        first = self.run_update()
        self.assertEqual(first.returncode, 0, first.stderr)
        actions = self.actual()["actions"]
        # When 使用同一个发布序号重跑
        retry = self.run_update()
        # Then 继续原容器且保留唯一一份升级前备份
        self.assertEqual(retry.returncode, 0, retry.stderr)
        self.assertEqual(self.actual()["actions"], actions)
        self.assertEqual(len(list(self.instance.glob(".houqin-backups/*/pre-update.tar"))), 1)
        self.assertEqual(self.db_file.read_text(), "已有数据")

    def test_takes_over_legacy_filename_even_when_image_is_already_current(self):
        # Given 已手工运行目标镜像，容器仍由优先级更高的旧文件名创建
        self.old.write_bytes(self.incoming.read_bytes())
        self.state.write_text(json.dumps(self.actual() | {"image": NEW, "image_id": NEW_ID}))
        # When CD 首次接管该实例
        result = self.run_update()
        # Then 标准文件名和容器项目标签都完成切换，此后重跑可复用当前容器
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(self.old.exists())
        self.assertEqual(self.actual()["file"], str(self.instance / "docker-compose.yaml"))
        self.assertEqual(self.run_update().returncode, 0)
        self.assertEqual(self.actual()["actions"].count("up"), 1)

    def test_late_finishing_release_does_not_downgrade(self):
        # Given 已开始部署更新的发布序号
        (self.instance / ".houqin-run-number").write_text("11\n")
        # When 较早发布的工作流较晚到达
        result = self.run_update(number=10)
        # Then 不拉取、不停机、不触动原实例
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], [])
        self.assert_old_instance()

    def test_start_failure_keeps_post_migration_data_and_emits_restore_steps(self):
        # Given 新容器能写数据但 Compose 在启动过程中报错
        # When 新版更新失败
        result = self.run_update(FAIL_AFTER_START="1")
        # Then 保留变动后的数据和升级前归档，提供针对本实例的恢复命令
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], ["pull", "stop", "archive", "up"])
        self.assertEqual(self.actual()["image"], NEW)
        self.assertEqual(self.db_file.read_text(), "已经开始迁移")
        [backup] = self.instance.glob(".houqin-backups/*/pre-update.tar")
        self.assertIn(str(backup), result.stderr)
        self.assertIn("stop openerp", result.stderr)
        self.assertIn("tar --numeric-owner --same-owner -xpf", result.stderr)
        self.assertIn("-f compose.yaml up", result.stderr)
        with tarfile.open(backup) as archive:
            self.assertEqual(archive.extractfile("data/postgres/records").read().decode(), "已有数据")

    def test_failed_backup_restarts_original_container(self):
        # Given 旧版可运行，但归档会在写入后报错
        # When 停机后归档失败
        result = self.run_update(FAIL_ARCHIVE="1")
        # Then 恢复旧容器，当前 Compose 和数据仍在，部分归档不占用磁盘
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.actual()["actions"], ["pull", "stop", "archive", "start"])
        self.assert_old_instance()
        self.assertFalse((self.instance / "docker-compose.yaml").exists())
        self.assertEqual(list(self.instance.glob(".houqin-backups/*/*")), [])

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

    def record(action):
        state["actions"].append(action)
        state_path.write_text(json.dumps(state))

    if command == "flock":
        return 0
    if command == "tar":
        assert "-tf" in args
        os.execv(os.environ["TEST_TAR"], ["tar", *args])
    if args[0] == "run":
        assert "--rm" in args and "--read-only" in args
        assert args[args.index("--network") + 1] == "none"
        assert args[args.index("--user") + 1] == "0:0"
        assert args[args.index("--mount") + 1] == f"type=bind,source={instance},target=/backup-source,readonly"
        position = args.index("--entrypoint")
        assert args[position + 1] == "tar" and args[position + 2] == state["image_id"]
        record("archive")
        if os.environ.get("FAIL_ARCHIVE"):
            sys.stdout.buffer.write(b"partial")
            return 1
        tar_args = args[position + 3:]
        tar_args[tar_args.index("-C") + 1] = str(instance)
        os.execv(os.environ["TEST_TAR"], ["tar", *tar_args])
    if args[0] == "compose":
        if "config" in args:
            os.execv(os.environ["TEST_DOCKER"], ["docker", *args])
        file = Path(args[args.index("-f") + 1])
        position = args.index("-f") + 2
        action = args[position]
        if action == "ps":
            print(state["id"])
            return 0
        if action == "stop":
            state.update(status="exited", exit=0)
            record("stop")
            return 0
        if action == "up":
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
            "State": {"Status": state["status"], "ExitCode": state["exit"], "OOMKilled": False, "Error": ""},
            "Image": state["image_id"],
        }))
        return 0
    if args[:2] == ["container", "start"]:
        state.update(status="running")
        record("start")
        return 0
    if args[:2] == ["image", "inspect"]:
        digest = OLD_ID if os.environ.get("WRONG_DIGEST") else NEW_DIGEST
        print(json.dumps({"Id": NEW_ID, "RepoDigests": [f"{NEW.rsplit(':', 1)[0]}@{digest}"]}))
        return 0
    if args[0] == "pull":
        record("pull")
        return 0
    raise RuntimeError(f"未知的测试命令：{command} {args}")


if __name__ == "__main__":
    if "TEST_STATE" in os.environ:
        sys.exit(run_fake_command())
    unittest.main()
