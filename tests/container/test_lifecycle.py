import os
from pathlib import Path
import shlex
import signal
import subprocess
import tempfile
import time
import unittest

ROOTFS = Path(__file__).resolve().parents[2] / "deploy/container/rootfs"
SCRIPTS = ROOTFS / "usr/local/libexec/openerp"


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        fixture = tempfile.TemporaryDirectory()
        self.addCleanup(fixture.cleanup)
        self.root = Path(fixture.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.events = self.root / "events"
        self.results = self.root / "results"
        self.results.mkdir()
        self.runtime = self.root / "runtime"
        self.runtime.mkdir()
        self.env = {
            **os.environ,
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "EVENTS": str(self.events),
            "OPENERP_APPLY_MIGRATIONS": "",
        }

    def stub(self, name, script):
        executable = self.bin / name
        executable.write_text("#!/bin/sh\nset -eu\n" + script)
        executable.chmod(0o755)

    def script_contents(self, name):
        script = SCRIPTS / name
        contents = script.read_text()
        contents = contents.replace("/run/openerp", str(self.runtime))
        contents = contents.replace("/run/s6-linux-init-container-results", str(self.results))
        contents = contents.replace("/run/s6/basedir/bin/halt", "halt-fixture")
        contents = contents.replace("kill -TERM 1", "term-fixture")
        contents = contents.replace("/app/efbundle", "efbundle-fixture")
        return contents

    def run_script(self, name, *args):
        return subprocess.run(
            ["/bin/sh", "-c", self.script_contents(name), name, *args],
            env=self.env, cwd=self.runtime, capture_output=True, text=True, timeout=5,
        )

    def migration_fixture(self, initial, after):
        (self.runtime / "appsettings.json").write_text("{}\n")
        self.env.update(INITIAL=str(initial), AFTER=str(after))
        self.stub("s6-setuidgid", 'shift\nexec "$@"\n')
        self.stub("dotnet", """
printf 'schema\n' >> "$EVENTS"
if [ -e bundle-ran ]; then
    exit "$AFTER"
fi
exit "$INITIAL"
""")
        self.stub("efbundle-fixture", """
test -f appsettings.json
printf 'bundle\n' >> "$EVENTS"
touch bundle-ran
""")

    def event_log(self):
        return self.events.read_text().splitlines() if self.events.exists() else []

    def test_empty_database_migrates_once_and_is_checked_again(self):
        # Given 空数据库，When 启动执行迁移步骤，Then 仅运行一次 bundle，复查之后才放行。
        self.migration_fixture(10, 0)
        result = self.run_script("migrate")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.event_log(), ["schema", "bundle", "schema"])

    def test_existing_pending_requires_explicit_opt_in(self):
        # Given 旧库有已知待迁移，When 未显式授权，Then 返回失败且 bundle 未运行。
        self.migration_fixture(11, 0)
        result = self.run_script("migrate")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.event_log(), ["schema"])

        # Given 相同数据库和 OPENERP_APPLY_MIGRATIONS=1，Then 迁移并再次核对。
        self.env["OPENERP_APPLY_MIGRATIONS"] = "1"
        result = self.run_script("migrate")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.event_log(), ["schema", "schema", "bundle", "schema"])

    def test_schema_failure_and_downgrade_never_apply_bundle(self):
        # Given 检查失败或数据库版本不可兼容，When 即使已授权，Then 启动失败，数据未改写。
        self.env["OPENERP_APPLY_MIGRATIONS"] = "1"
        for status in (1, 12):
            with self.subTest(status=status):
                self.events.unlink(missing_ok=True)
                self.migration_fixture(status, 0)
                self.assertNotEqual(self.run_script("migrate").returncode, 0)
                self.assertEqual(self.event_log(), ["schema"])

    def test_matching_schema_starts_without_running_bundle(self):
        # Given 版本完全相符，When 启动，Then 直接放行并保持已有库内容。
        self.migration_fixture(0, 0)
        self.assertEqual(self.run_script("migrate").returncode, 0)
        self.assertEqual(self.event_log(), ["schema"])

    def test_bundle_success_requires_a_matching_schema(self):
        # Given bundle 退出 0 但版本仍不相符，When 复查，Then 阻止 API 启动。
        self.migration_fixture(10, 11)
        self.assertNotEqual(self.run_script("migrate").returncode, 0)
        self.assertEqual(self.event_log(), ["schema", "bundle", "schema"])

    def readiness_fixture(self):
        (self.runtime / "postgres-password").write_text("postgres-test-secret")
        (self.runtime / "redis-password").write_text("redis-test-secret")
        self.stub("timeout", 'shift\nexec "$@"\n')
        self.stub("sleep", 'printf "wait\\n" >> "$EVENTS"\n')
        self.stub("s6-setuidgid", 'shift\nexec "$@"\n')
        self.stub("psql", """
test "$PGPASSWORD" = postgres-test-secret
case " $* " in
    *' --host=127.0.0.1 '*--command='SELECT 1'*) ;;
    *) exit 1 ;;
esac
printf 'postgres\n' >> "$EVENTS"
printf '1\n'
""")
        self.stub("redis-cli", """
test "$REDISCLI_AUTH" = redis-test-secret
case " $* " in
    *' -a '*|*' --pass '*) exit 1 ;;
esac
if [ -e redis-authenticated ]; then
    printf 'redis-ready\n' >> "$EVENTS"
    printf 'PONG\n'
else
    printf 'redis-auth-failed\n' >> "$EVENTS"
    printf 'NOAUTH Authentication required.\n'
    touch redis-authenticated
fi
""")
        self.stub("dotnet", """
test "$2" = container-storage-ready
if [ -e storage-probed ]; then
    printf 'storage-ready\n' >> "$EVENTS"
else
    printf 'storage-unavailable\n' >> "$EVENTS"
    touch storage-probed
    exit 1
fi
""")

    def test_readiness_waits_for_all_protocols(self):
        # Given Redis 拒绝认证且对象存储暂不可用，When 就绪检查重试，Then 三个协议都成功后才放行。
        self.readiness_fixture()
        result = self.run_script("ready")
        self.assertEqual(result.returncode, 0, result.stderr)
        events = self.event_log()
        self.assertIn("postgres", events)
        self.assertLess(events.index("redis-auth-failed"), events.index("redis-ready"))
        self.assertLess(events.index("storage-unavailable"), events.index("storage-ready"))
        self.assertIn("wait", events)
        self.assertNotIn("test-secret", result.stdout + result.stderr)

    def test_readiness_stops_when_a_critical_process_has_exited(self):
        # Given 数据服务已报意外退出，When 就绪检查运行，Then 立即阻止迁移。
        self.readiness_fixture()
        (self.runtime / "service-failed").mkdir()
        self.assertNotEqual(self.run_script("ready").returncode, 0)
        self.assertEqual(self.event_log(), [])

    def test_readiness_oneshot_times_out_on_persistent_authentication_failure(self):
        # Given Redis 一直拒绝认证，When 启动预算耗尽，Then 就绪 oneshot 非零退出。
        self.readiness_fixture()
        self.stub("dotnet", 'test "$2" = container-storage-ready\n')
        self.stub("redis-cli", """
printf 'redis-auth-failed\n' >> "$EVENTS"
printf 'NOAUTH Authentication required.\n'
""")
        self.stub("sleep", 'printf "wait\\n" >> "$EVENTS"\nexec /bin/sleep 30\n')
        script = self.runtime / "ready"
        script.write_text(self.script_contents("ready"))
        ready_command = ["/bin/sh", str(script)]
        process_options = {
            "env": self.env, "cwd": self.runtime, "stdout": subprocess.PIPE,
            "stderr": subprocess.PIPE, "text": True, "start_new_session": True,
        }

        # 等到脚本进入重试，再检查诊断。缩短后的 timeout 不承担调度探针的时间预算。
        with subprocess.Popen(ready_command, **process_options) as probe:
            try:
                deadline = time.monotonic() + 10
                while "wait" not in self.event_log():
                    self.assertIsNone(probe.poll(), "Redis 认证失败时就绪检查提前退出。")
                    self.assertLess(time.monotonic(), deadline, "就绪检查未进入重试。")
                    time.sleep(0.025)
            finally:
                if probe.poll() is None:
                    os.killpg(probe.pid, signal.SIGKILL)
                stdout, _ = probe.communicate(timeout=3)
        self.assertIn("redis-auth-failed", self.event_log())
        self.assertIn("Waiting for: redis", stdout)

        command = shlex.split((ROOTFS / "etc/s6-overlay/s6-rc.d/ready/up").read_text())
        if command[0] == "/usr/bin/timeout":
            # 使用真实 timeout，只缩短该 fixture 的等待时间。
            command[-2] = "1"
        command[-1:] = ready_command
        with subprocess.Popen(command, **process_options) as oneshot:
            try:
                _, stderr = oneshot.communicate(timeout=6)
            except subprocess.TimeoutExpired:
                os.killpg(oneshot.pid, signal.SIGKILL)
                oneshot.communicate(timeout=3)
                self.fail("就绪 oneshot 超出预算后仍未退出。")
        self.assertEqual(oneshot.returncode, 124, stderr)

    def finish_fixture(self, wanted_up):
        self.env["WANTED_UP"] = wanted_up
        self.stub("s6-svstat", 'printf "%s\n" "$WANTED_UP"\n')
        self.stub("halt-fixture", 'printf "halt\n" >> "$EVENTS"\n')

    def test_unexpected_success_exit_disables_restart_and_halts_nonzero(self):
        # Given supervisor 仍要求进程保持运行，When 进程意外退出 0，Then 标记失败并退出且不重启。
        self.finish_fixture("true")
        result = self.run_script("finish", "postgres", "0", "0")
        self.assertEqual(result.returncode, 125, result.stderr)
        self.assertEqual(self.event_log(), ["halt"])
        self.assertNotEqual((self.results / "exitcode").read_text().strip(), "0")

    def test_halt_command_failure_cannot_reenable_restart(self):
        # Given halt 请求返回错误，When 意外退出的进程清理，Then 仍禁止自动拉起。
        self.finish_fixture("true")
        self.stub("halt-fixture", "exit 111\n")
        self.stub("term-fixture", 'printf "terminate\\n" >> "$EVENTS"\n')
        result = self.run_script("finish", "redis", "256", "9")
        self.assertEqual(result.returncode, 125)
        self.assertNotEqual((self.results / "exitcode").read_text().strip(), "0")
        self.assertEqual(self.event_log(), ["terminate"])

    def test_requested_stop_leaves_container_exit_code_untouched(self):
        # Given supervisor 正在按序停服务，When 进程收到停止信号，Then finish 不覆盖退出码。
        self.finish_fixture("false")
        result = self.run_script("finish", "postgres", "256", "2")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.event_log(), [])
        self.assertFalse((self.results / "exitcode").exists())


if __name__ == "__main__":
    unittest.main()
