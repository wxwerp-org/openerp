#!/usr/bin/env bash
set -euo pipefail
umask 077

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

(( $# == 5 )) || fail "用法：$0 INSTANCE_DIR INCOMING_COMPOSE EXPECTED_IMAGE RUN_NUMBER EXPECTED_DIGEST"
incoming=$2
expected_image=$3
run_number=$4
expected_digest=$5
[[ ${expected_image##*/} == *:* && $expected_image != -* && $expected_image != *@* && $expected_image != *[[:space:]]* ]] \
    || fail '目标镜像必须带有 Release 标签。'
[[ $expected_digest =~ ^sha256:[0-9a-f]{64}$ ]] || fail '无效的 Release 镜像摘要。'
[[ -f $incoming && -s $incoming ]] || fail '缺少 Release Compose 附件。'
[[ -d $1 ]] || fail '实例目录不存在。'
instance=$(cd -- "$1" && pwd -P)
exec 9<"$instance"
flock -n 9 || fail '此实例正在更新，或无法锁定实例。'

work=$(mktemp -d "$instance/.houqin-update.XXXXXXXX")
cleanup() {
    local status=$? message=
    trap '' HUP INT TERM
    if (( status != 0 )) && [[ -f $work/previous-compose.yaml && ! -e $work/docker-compose.yaml ]]; then
        if restore_compose; then
            message='更新失败，已还原切换前的 Compose 文件。'
        else
            message='更新失败，未还原实例的 Compose 文件。'
        fi
    fi
    rm -rf -- "$work" || true
    [[ -z $message ]] || printf '%s\n' "$message" >&2 || true
    exit "$status"
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM
install -m 0644 -- "$incoming" "$work/docker-compose.yaml"
cd -- "$instance"

compose() {
    docker compose --project-directory "$instance" -f "$1" "${@:2}"
}

[[ $run_number =~ ^[1-9][0-9]{0,17}$ ]] || fail '无效的发布运行序号。'
marker=.houqin-run-number
[[ ! -L $marker ]] || fail '部署状态路径不能是符号链接。'
if [[ -e $marker ]]; then
    [[ -f $marker ]] || fail '无法读取已开始部署的发布序号。'
    last_run=$(cat -- "$marker")
    [[ $last_run =~ ^[1-9][0-9]{0,17}$ ]] || fail '已记录的发布运行序号无效。'
    (( run_number >= last_run )) || fail "发布序号 $run_number 早于已开始部署的 ${last_run}。"
fi

next_config=$(compose "$work/docker-compose.yaml" config --format json)
jq -e --arg image "$expected_image" '
    (.services | keys) == ["openerp"] and .services.openerp.image == $image
' <<< "$next_config" >/dev/null || fail 'Release 附件必须只部署目标版本的 openerp。'
[[ -d data/postgres && ! -L data && ! -L data/postgres ]] || fail '实例缺少原数据目录。'
[[ ! -L .env && ( ! -e .env || -f .env ) ]] || fail '实例的 .env 必须是普通文件。'

record_run() {
    printf '%s\n' "$run_number" > "$work/run-number"
    mv -f -- "$work/run-number" "$marker"
}

current=docker-compose.yaml
[[ -f $current && ! -L $current ]] || fail '缺少原实例的 Compose 文件。'
current_config=$(compose "$current" config --format json)
project=$(jq -er '.name' <<< "$current_config")
current_image=$(jq -er '.services.openerp.image' <<< "$current_config")

validate_config() {
    jq -e --arg path "$instance" --arg project "$project" '
        .name == $project and (.services | keys) == ["openerp"] and
        (.services.openerp |
            (.build == null) and ((.profiles // []) == []) and
            (.ports | length == 1 and .[0].host_ip == "127.0.0.1" and .[0].target == 8000) and
            ([.volumes[] | [.type, .source, .target, (.read_only // false)]] | sort) == [
                ["bind", ($path + "/data"), "/var/lib/openerp", false],
                ["bind", ($path + "/data/postgres"), "/var/lib/postgresql", false]
            ]
        )
    ' >/dev/null
}
validate_config <<< "$current_config" || fail '原 Compose 项目不符合单容器实例的部署约定。'
validate_config <<< "$next_config" || fail 'Release 附件必须沿用原 Compose 项目、回环端口和数据目录。'
[[ $(jq -cS '.services.openerp.ports' <<< "$current_config") == "$(jq -cS '.services.openerp.ports' <<< "$next_config")" ]] \
    || fail 'Release 附件改变了实例的 Web 端口。'

container_id() {
    local id
    id=$(compose "$1" ps --all --quiet) || return
    [[ -n $id && $id != *$'\n'* ]] || fail 'Compose 项目必须恰有一个现有容器。'
    printf '%s\n' "$id"
}

inspect() {
    docker container inspect --format '{{json .}}' "$1"
}

restore_compose() {
    local id info started_at
    id=$(container_id "$current") || return
    [[ $id == "$current_id" ]] || return 1
    info=$(inspect "$id") || return
    started_at=$(jq -er '.State.StartedAt | strings | select(length > 0)' <<< "$current_info") || return
    jq -e --arg started_at "$started_at" '
        .State.Status == "running" and .State.StartedAt == $started_at
    ' <<< "$info" >/dev/null || return
    mv -f -- "$work/previous-compose.yaml" "$current"
}

current_id=$(container_id "$current")
current_info=$(inspect "$current_id")
jq -e --arg path "$instance" --arg project "$project" --arg file "$instance/$current" --arg image "$current_image" '
    .Config.Image == $image and
    (.Config.Labels | .["com.docker.compose.project"] == $project and
        .["com.docker.compose.service"] == "openerp" and
        .["com.docker.compose.project.config_files"] == $file) and
    ([.Mounts[] | [.Type, .Source, .Destination, .RW]] | sort) == [
        ["bind", ($path + "/data"), "/var/lib/openerp", true],
        ["bind", ($path + "/data/postgres"), "/var/lib/postgresql", true]
    ]
' <<< "$current_info" >/dev/null || fail '现有容器与原 Compose 项目或数据挂载不匹配。'
actual_port=$(jq -er '
    .HostConfig.PortBindings["8000/tcp"] |
    select(length == 1 and .[0].HostIp == "127.0.0.1") | .[0].HostPort | select(test("^[0-9]+$"))
' <<< "$current_info") || fail '原容器没有绑定到约定的回环端口。'
[[ $(jq -er '.services.openerp.ports[0].published | tostring' <<< "$current_config") == "$actual_port" ]] \
    || fail "当前 Compose 端口与原容器不同，请在实例 .env 中持久化 WEB_PORT=$actual_port 后重试。"
jq -e '.State.Status == "running"' <<< "$current_info" >/dev/null \
    || fail '原实例处于停止或异常状态，请确认实例状态后再运行部署。'

release_image_id() {
    local image_info digest_ref="${expected_image%:*}@$expected_digest"
    image_info=$(docker image inspect --format '{{json .}}' "$expected_image") || return
    jq -er --arg digest "$digest_ref" 'select((.RepoDigests // []) | index($digest)) | .Id' <<< "$image_info" \
        || fail "本地镜像的清单摘要与发布不匹配：$digest_ref"
}

docker pull "$expected_image"
expected_id=$(release_image_id)

cp -p -- "$current" "$work/previous-compose.yaml"
record_run
mv -f -- "$work/docker-compose.yaml" "$current" || fail '无法替换实例的 Compose 文件。'
compose "$current" up -d --pull never --no-build
new_id=$(container_id "$current")
jq -e --arg image "$expected_image" --arg id "$expected_id" '
    .State.Status == "running" and .Config.Image == $image and .Image == $id
' <<< "$(inspect "$new_id")" >/dev/null || fail '运行容器的镜像与本次拉取的 Release 镜像不匹配。'
printf '实例已运行镜像 %s（%s）。\n' "$expected_image" "$expected_id"

# 部署成功后清理旧版本镜像，避免占满服务器磁盘：只删同一镜像仓库前缀下、不是本次发布的已打标签镜像。
# 其他项目（如 wxw 系列）的镜像不会被碰；仍被容器使用的镜像 docker 会拒绝删除。清理失败不影响本次部署。
cleanup_old_images() {
    local prefix="${expected_image%/openerp:*}" ref removed=0
    [[ -n $prefix && $prefix != "$expected_image" ]] || return 0
    while read -r ref; do
        [[ $ref == "$prefix"/* && $ref != "$expected_image" && $ref != *"<none>"* ]] || continue
        if docker image rm "$ref" >/dev/null 2>&1; then
            removed=$((removed + 1))
        fi
    done < <(docker images --format '{{.Repository}}:{{.Tag}}')
    printf '已清理旧版本镜像 %d 个。\n' "$removed"
}
cleanup_old_images || printf '旧镜像清理未完成，已忽略。\n'
