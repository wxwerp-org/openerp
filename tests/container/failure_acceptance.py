"""在各自隔离的 Compose 项目中验证初始化、迁移和磁盘故障。"""

import argparse
from http.client import HTTPException
import json
from pathlib import Path
import tempfile
import time

from acceptance import (
    Acceptance,
    CheckFailed,
    LATEST_MIGRATION,
    MIGRATIONS_SQL,
    SERVICE,
    START_TIMEOUT,
    TOOL_LABEL,
    VOLUMES,
    expect,
)


DDL_FAILURE = "openerp-fixture-ddl-denied"
INIT_CHECKPOINT = "openerp-fixture-init-incomplete"
DISK_FAILURE = "openerp-fixture-tmpfs-enospc"
INIT_HOOK = """
#!/bin/sh
set -eu

# 模拟 PG_VERSION 已写入、目标数据库尚未创建的初始化检查点。
psql --no-password --no-psqlrc -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" \
    --dbname=postgres --command="DROP DATABASE openerp"
printf 'openerp-fixture-init-incomplete\n'
exec sleep 240
""".lstrip()
DISK_HOOK = """
#!/bin/sh
set -eu

# 只填充本测试的单页 tmpfs；应用应在写入运行配置时失败。
export LC_ALL=C
if dd if=/dev/zero of=/run/openerp/scratch bs=4096 count=2 2>/tmp/openerp-fixture-dd.log; then
    printf 'fixture could not exhaust the tmpfs\n' >&2
    exit 1
fi
grep -F 'No space left on device' /tmp/openerp-fixture-dd.log
printf 'openerp-fixture-tmpfs-enospc\n'
""".lstrip()
PG_MAINTENANCE = """
export PGDATA=/var/lib/postgresql/18/docker
socket=$(mktemp -d)
chown postgres:postgres "$socket"
trap '/command/s6-setuidgid postgres pg_ctl -D "$PGDATA" -m fast -t 30 -w stop >&2' EXIT
if ! /command/s6-setuidgid postgres pg_ctl -D "$PGDATA" -l "$socket/postgres.log" -t 45 -w \
    -o "-c listen_addresses='' -c unix_socket_directories=$socket" start >&2; then
    cat "$socket/postgres.log" >&2
    exit 1
fi
export PGPASSWORD="$(cat /fixture-secrets/postgres-password)"
psql -wXqAt --host="$socket" --username=openerp --dbname=openerp -v ON_ERROR_STOP=1
"""


class FailureAcceptance(Acceptance):
    def __init__(self, args, temporary):
        super().__init__(args, temporary)
        self.override = temporary / "compose.fault.json"
        self.fixture = temporary / "fault.sh"
        self.evidence = {}

    def compose(self, *arguments, timeout=30):
        if not self.override.exists():
            return super().compose(*arguments, timeout=timeout)
        return self.docker(
            "compose", "--project-name", self.project, "--env-file", str(self.dotenv),
            "-f", str(self.args.compose), "-f", str(self.override), *arguments, timeout=timeout,
        )

    def inject(self, target, contents, **settings):
        self.fixture.write_text(contents)
        self.fixture.chmod(0o755)
        mount = {"type": "bind", "source": str(self.fixture), "target": target, "read_only": True}
        self.override.write_text(json.dumps({"services": {SERVICE: {"volumes": [mount], **settings}}}))

    def start_fixture(self, *, recreate=False):
        # 先创建，确保在第一条故障路径执行前就禁用业务容器的自动重启。
        self.up(recreate=recreate, no_start=True)
        self.docker("start", self.service_id)

    def failed_start(self, *diagnostics):
        stopped = self.wait(exit_code="nonzero", timeout=START_TIMEOUT)
        logs = self.logs_since(stopped["State"]["StartedAt"])
        evidence = {"exitCode": stopped["State"]["ExitCode"], "observedDiagnostics": []}
        self.evidence["refusedStart"] = evidence
        for diagnostic in diagnostics:
            expect(diagnostic in logs, f"故障启动未观察到诊断 {diagnostic}。")
            evidence["observedDiagnostics"].append(diagnostic)
        expect("service api: starting" not in logs and "service api successfully started" not in logs,
               "故障启动期间 s6 尝试开启 API。")
        evidence["apiStartObserved"] = False

    def wait_for_init_checkpoint(self):
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            state = self.inspect()["State"]
            expect(state["Running"], "PostgreSQL 初始化尚未到达注入点，容器已经退出。")
            if INIT_CHECKPOINT in self.logs_since(state["StartedAt"]):
                return
            time.sleep(1)
        raise CheckFailed("等待 PostgreSQL 初始化注入点超过 120 秒。")

    def assert_api_port_closed(self):
        self.execute(
            "/bin/sh", "-c",
            "curl --silent --output /dev/null --max-time 5 http://127.0.0.1:8081/login; test \"$?\" -eq 7",
        )

    def interrupted_initialization(self):
        self.inject("/docker-entrypoint-initdb.d/00-openerp-fixture.sh", INIT_HOOK)
        self.start_fixture()
        self.wait_for_init_checkpoint()
        self.assert_api_port_closed()
        self.evidence["initialCheckpoint"] = {"observed": INIT_CHECKPOINT, "apiPortOpen": False}

        self.docker("kill", "--signal", "KILL", self.service_id)
        stopped = self.wait(exit_code="nonzero", timeout=20)
        expect(stopped["State"]["ExitCode"] == 137, "初始化容器未按预期收到 SIGKILL。")
        self.evidence["initialCheckpoint"]["exitCode"] = 137
        self.checked_volume_names("exited")
        self.override.unlink()
        self.start_fixture(recreate=True)
        self.failed_start("Skipping initialization", "[ready] Waiting for: postgres",
                          "start service ready: command exited 124")

    def disk_full(self):
        hook = "/openerp-fixture-enospc.sh"
        self.inject(
            hook,
            DISK_HOOK,
            environment={"S6_STAGE2_HOOK": hook},
            tmpfs=["/run/openerp:rw,nosuid,nodev,noexec,size=4096,mode=0711"],
        )
        self.start_fixture()
        self.failed_start(DISK_FAILURE, "No space left on device", "无法生成容器运行配置")
        self.evidence["tmpfsLimitBytes"] = 4096

    def offline_postgres(self, statement):
        volumes = self.checked_volume_names("exited")
        mounts = ["--mount", f"type=bind,source={self.secret_dir},target=/fixture-secrets,readonly"]
        for key, path in VOLUMES.items():
            spec = f"type=volume,source={volumes[key]},target={path},volume-nocopy"
            mounts += ["--mount", spec if key == "postgres" else spec + ",readonly"]
        self.used_archive_tool = True
        return self.docker(
            "run", "--rm", "--name", f"{self.project}-pg-fixture", "--network", "none", "-i",
            "--pull", "never", "--platform", "linux/amd64", "--user", "0:0", "--entrypoint", "/bin/sh",
            "--label", f"{TOOL_LABEL}={self.project}", *mounts, self.image_id, "-ec", PG_MAINTENANCE,
            data=statement.encode(), timeout=120,
        ).decode().splitlines()

    def failed_migration(self):
        self.first_start()
        self.store_data()
        migrations = self.migration_ids()
        expect(migrations[-1] == LATEST_MIGRATION, "迁移注入需随最新迁移更新。")
        self.sql(f"""
BEGIN;
ALTER TABLE "AdminAccounts" DROP COLUMN "MustChangePassword";
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{LATEST_MIGRATION}';
CREATE FUNCTION acceptance_fail_ddl() RETURNS event_trigger LANGUAGE plpgsql AS $$
BEGIN
    UPDATE "AdminAccounts" SET "Name" = 'fixture changed' WHERE "Id" = 1;
    RAISE EXCEPTION '{DDL_FAILURE}';
END;
$$;
CREATE EVENT TRIGGER acceptance_fail_ddl ON ddl_command_end
    WHEN TAG IN ('ALTER TABLE') EXECUTE FUNCTION acceptance_fail_ddl();
COMMIT;
""")
        self.configure_env(apply_migrations=True)
        self.start_fixture(recreate=True)
        self.failed_start("Applying pending migrations", DDL_FAILURE)

        # 通过同镜像维护容器读取失败后的数据库。此时业务容器及其 PostgreSQL 均已退出。
        observed = self.offline_postgres(MIGRATIONS_SQL + """
SELECT "Name" FROM "AdminAccounts" WHERE "Id" = 1;
SELECT count(*) FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'AdminAccounts' AND column_name = 'MustChangePassword';
DROP EVENT TRIGGER acceptance_fail_ddl;
DROP FUNCTION acceptance_fail_ddl();
""")
        expect(observed == [*migrations[:-1], self.profile_name, "0"],
               "迁移失败后原有资料、迁移历史或 DDL 的回滚状态不正确。")
        self.evidence["migrationRolledBack"] = True
        self.docker("start", self.service_id)
        self.healthy()
        expect(self.sql(MIGRATIONS_SQL).splitlines() == migrations, "清除注入后未能升级数据库。")
        self.verify_persistent_data("迁移失败并重试")
        self.evidence["preservedData"] = ["administrator-profile", "session", "redis", "s3-object"]


CASES = {
    "migration": FailureAcceptance.failed_migration,
    "initialization": FailureAcceptance.interrupted_initialization,
    "enospc": FailureAcceptance.disk_full,
}


def run_case(args, name):
    with tempfile.TemporaryDirectory(prefix=f"openerp-{name}-", dir="/tmp") as directory:
        fixture = FailureAcceptance(args, Path(directory))
        result = {"case": name, "project": fixture.project, "steps": fixture.steps, "evidence": fixture.evidence}
        success = False
        try:
            fixture.step(f"{name}：核对发布 Compose、镜像和项目隔离", fixture.preflight)
            fixture.step(f"{name}：注入并验证实际故障", lambda: CASES[name](fixture))
            success = True
        except (CheckFailed, HTTPException, OSError, ValueError, KeyError) as error:
            result["error"] = fixture.redact(str(error))
            print(f"[failure-acceptance] {result['error']}", flush=True)
            if fixture.service_id:
                try:
                    result["failureLogs"] = fixture.redact(fixture.docker(
                        "logs", "--tail", "150", fixture.service_id, combine=True,
                    ).decode(errors="replace"))
                except (CheckFailed, OSError):
                    result["failureLogs"] = "无法取得容器日志。"
        finally:
            try:
                fixture.clean_up()
            except (CheckFailed, OSError) as error:
                result["cleanupError"] = fixture.redact(str(error))
            result["passed"] = success and "cleanupError" not in result
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compose", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--case", action="append", choices=CASES, help="可重复，默认执行三项")
    args = parser.parse_args()
    if not args.compose.is_absolute() or not args.compose.is_file():
        parser.error("--compose 必须是已有 Compose 文件的绝对路径")

    results = []
    selected = args.case or list(CASES)
    for name in selected:
        results.append(run_case(args, name))
        report = {
            "image": args.image, "requestedCases": selected, "cases": results,
            "passed": len(results) == len(selected) and all(result["passed"] for result in results),
        }
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
