#!/usr/bin/env bash
set -euo pipefail

readonly data=/var/lib/openerp
readonly pg_credentials="$data/credentials/postgres"
readonly s3_credentials="$data/credentials/s3"
readonly weed_data="$data/seaweedfs"
readonly redis_data="$data/redis"
readonly api=/app/OpenERP.Api.dll
readonly startup_timeout=90

declare -A pids=()
migration_pid=
postgres_deadline=0
stop_requested=0

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

stop_if_requested() {
    if (( stop_requested )); then
        exit 0
    fi
}

has_data() (
    shopt -s dotglob nullglob
    local entries=("$1"/*)
    (( ${#entries[@]} > 0 ))
)

stop_child() {
    local pid=$1 signal=$2
    if [[ -n "$pid" ]]; then
        kill -s "$signal" "$pid" 2>/dev/null || true
        wait "$pid" 2>/dev/null || true
    fi
}

shutdown() {
    local status=$?
    local postgres_pid=${pids[PostgreSQL]-}
    trap '' TERM INT
    printf '%s\n' '正在停止 OpenERP 服务'
    stop_child "${pids[Caddy]-}" TERM
    stop_child "${pids[API]-}" TERM
    stop_child "$migration_pid" TERM
    stop_child "${pids[SeaweedFS]-}" TERM
    stop_child "${pids[Redis]-}" TERM
    if [[ -n "$postgres_pid" ]]; then
        # 首启的官方入口先使用仅监听 Unix socket 的临时实例；给它时间完成建库。
        while (( SECONDS < postgres_deadline )) && kill -0 "$postgres_pid" 2>/dev/null \
            && [[ $(cat "/proc/$postgres_pid/comm" 2>/dev/null) != postgres ]]; do
            sleep 1
        done
        stop_child "$postgres_pid" INT
    fi
    exit "$status"
}

check_children() {
    stop_if_requested
    local service
    for service in "${!pids[@]}"; do
        kill -0 "${pids[$service]}" 2>/dev/null || fail "$service 意外退出。"
    done
}

wait_ready() {
    local service=$1 deadline=$((SECONDS + startup_timeout))
    shift
    while true; do
        check_children
        if "$@"; then
            return
        fi
        (( SECONDS < deadline )) || fail "$service 就绪检查超时。"
        sleep 1
    done
}

postgres_ready() {
    pg_isready -q -h 127.0.0.1 -p 5432 -d "$POSTGRES_DB" -U "$POSTGRES_USER" -t 1
}

redis_ready() {
    redis-cli -h 127.0.0.1 ping 2>/dev/null | grep -Fx PONG >/dev/null
}

http_probe() {
    curl --fail --silent --max-time 2 --noproxy '*' "$@"
}

weed_ready() {
    http_probe -o /dev/null http://127.0.0.1:8333/readyz \
        && http_probe http://127.0.0.1:9333/dir/status \
        | grep -E '"DataNodes"[[:space:]]*:[[:space:]]*\[[[:space:]]*\{' >/dev/null
}

# 凭据组与各自的数据配套。缺失时先失败，防止为已有数据生成另一组凭据。
trap 'stop_requested=1' TERM INT
if [[ ! -d "$pg_credentials/current" ]] && has_data "$PGDATA"; then
    fail "已有 PostgreSQL 数据，但缺少 PostgreSQL 凭据组 $pg_credentials/current。请从配套备份恢复凭据。"
fi
if [[ ! -d "$s3_credentials/current" ]] && has_data "$weed_data"; then
    fail "已有 SeaweedFS 数据，但缺少 S3 凭据组 $s3_credentials/current。请从配套备份恢复凭据。"
fi

install -d -m 0711 "$data/credentials"
install -d -m 0700 "$pg_credentials" "$s3_credentials"
dotnet "$api" initialize-compose-credentials "$pg_credentials" "$s3_credentials"
chgrp openerp-api "$pg_credentials" "$pg_credentials/current"
chmod 0710 "$pg_credentials"
chmod 0750 "$pg_credentials/current"
chmod 0400 "$POSTGRES_PASSWORD_FILE"
chown openerp-api "$PGPASSFILE"
chmod 0600 "$PGPASSFILE"
chmod 0711 "$s3_credentials" "$s3_credentials/current"
chown openerp-weed "$s3_credentials/current/s3.json"
chmod 0400 "$s3_credentials/current/s3.json"
chown openerp-api "$s3_credentials/current/client" "$s3_credentials/current/client"/S3__{AccessKey,SecretKey}
chmod 0700 "$s3_credentials/current/client"
chmod 0400 "$s3_credentials/current/client"/S3__{AccessKey,SecretKey}
install -d -m 0700 -o openerp-redis -g openerp-redis "$redis_data"
install -d -m 0700 -o openerp-weed -g openerp-weed "$weed_data"

stop_if_requested
trap shutdown EXIT
postgres_deadline=$((SECONDS + startup_timeout))
POSTGRES_HOST_AUTH_METHOD=scram-sha-256 \
    env --default-signal=INT docker-entrypoint.sh postgres -c listen_addresses=127.0.0.1 &
pids[PostgreSQL]=$!
wait_ready PostgreSQL postgres_ready

gosu openerp-api /app/efbundle --connection "$ConnectionStrings__openerp" &
migration_pid=$!
while kill -0 "$migration_pid" 2>/dev/null; do
    check_children
    sleep 1
done
wait "$migration_pid" || fail '数据库迁移失败。'
migration_pid=
check_children

gosu openerp-redis redis-server --bind 127.0.0.1 --dir "$redis_data" --save 60 1 &
pids[Redis]=$!
# master 与 filer 的默认元数据位置沿用 -dir，和 volume 数据一起持久化。
gosu openerp-weed weed server -dir="$weed_data" -ip=127.0.0.1 -ip.bind=127.0.0.1 \
    -volume.port=8081 -volume.max=8 -master.volumeSizeLimitMB=30720 \
    -filer -s3 -s3.config="$s3_credentials/current/s3.json" &
pids[SeaweedFS]=$!
wait_ready Redis redis_ready
wait_ready SeaweedFS weed_ready

OPENERP_S3_CREDENTIALS_DIRECTORY="$s3_credentials" gosu openerp-api dotnet "$api" &
pids[API]=$!
wait_ready API http_probe -o /dev/null http://127.0.0.1:8080/api/auth/status

gosu openerp-web caddy run --config /etc/caddy/Caddyfile --adapter caddyfile &
pids[Caddy]=$!
trap 'exit 0' TERM INT
check_children

# 任一常驻服务退出都重启整套实例。即使子进程自行成功退出，也视为故障。
status=0
wait -n -p exited_pid || status=$?
for service in "${!pids[@]}"; do
    if [[ "${pids[$service]}" == "${exited_pid-}" ]]; then
        fail "$service 意外退出（状态码 $status）。"
    fi
done
fail "服务意外退出（状态码 $status）。"
