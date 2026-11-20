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
phase=prepare
cleanup() {
    local status=$?
    trap '' INT TERM
    if (( status != 0 )) && [[ $phase == stopping ]]; then
        rm -f -- "$archive.partial" || true
        rmdir -- "$backup" 2>/dev/null || true
        printf '%s\n' '更新在启动新版前失败，检查并恢复原容器。' >&2
        case $(inspect "$current_id" | jq -r '.State.Status') in
            running) ;;
            exited)
                if ! docker container start "$current_id"; then
                    printf '请检查容器后执行 docker container start %q。\n' "$current_id" >&2
                fi
                ;;
            *)
                printf '请检查原容器 %s 的状态。\n' "$current_id" >&2
                ;;
        esac
    elif (( status != 0 )) && [[ $phase == apply ]]; then
        print_recovery >&2
    fi
    rm -rf -- "$work" || true
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
install -m 0644 -- "$incoming" "$work/docker-compose.yaml"
cd -- "$instance"

compose() {
    docker compose --project-directory "$instance" -f "$1" "${@:2}"
}

[[ $run_number =~ ^[1-9][0-9]{0,17}$ ]] || fail '无效的发布运行序号。'
marker=.houqin-run-number
backup_root=.houqin-backups
[[ ! -L $marker && ! -L $backup_root ]] || fail '部署状态路径不能是符号链接。'
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
if [[ -e compose.yaml || -L compose.yaml ]]; then
    current=compose.yaml
fi
[[ -f $current && ! -L $current ]] || fail '缺少原实例的 Compose 文件。'
[[ ! -L docker-compose.yaml && ( ! -e docker-compose.yaml || -f docker-compose.yaml ) ]] \
    || fail 'docker-compose.yaml 必须是普通文件。'
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
    id=$(compose "$1" ps --all --quiet)
    [[ -n $id && $id != *$'\n'* ]] || fail 'Compose 项目必须恰有一个现有容器。'
    printf '%s\n' "$id"
}

inspect() {
    docker container inspect --format '{{json .}}' "$1"
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
    || fail '原实例处于停止或异常状态，请确认实例和现有备份后再运行部署。'

release_image_id() {
    local image_info digest_ref="${expected_image%:*}@$expected_digest"
    image_info=$(docker image inspect --format '{{json .}}' "$expected_image") || return
    jq -er --arg digest "$digest_ref" 'select((.RepoDigests // []) | index($digest)) | .Id' <<< "$image_info" \
        || fail "本地镜像的清单摘要与发布不匹配：$digest_ref"
}

current_image_id=$(jq -er '.Image' <<< "$current_info")
if [[ $current == docker-compose.yaml && $current_image == "$expected_image" ]]; then
    # 发布附件和镜像标签按版本固定，工作流重试无需再次停机和备份。
    [[ $(jq -cS . <<< "$current_config") == "$(jq -cS . <<< "$next_config")" ]] \
        || fail '同一版本的已安装 Compose 与 Release 附件不同，请检查实例配置。'
    expected_id=$(release_image_id)
    [[ $expected_id == "$current_image_id" ]] \
        || fail '当前容器的镜像 ID 与本地 Release 标签不匹配。'
    record_run
    printf '实例已运行镜像 %s（%s）。\n' "$expected_image" "$current_image_id"
    exit 0
fi

docker pull "$expected_image"
expected_id=$(release_image_id)

mkdir -p -- "$backup_root"
chmod 0700 "$backup_root"
backup=$(mktemp -d "$backup_root/$(date -u +%Y%m%dT%H%M%SZ)-XXXXXXXX")
backup="$instance/$backup"
archive="$backup/pre-update.tar"

record_run
phase=stopping
compose "$current" stop openerp
jq -e '.State | .Status == "exited" and .ExitCode == 0 and .OOMKilled == false and .Error == ""' \
    <<< "$(inspect "$current_id")" >/dev/null || fail '未确认容器干净停止。'

backup_files=("$current" data)
if [[ $current == compose.yaml && -f docker-compose.yaml ]]; then
    backup_files+=(docker-compose.yaml)
fi
if [[ -f .env ]]; then
    backup_files+=(.env)
fi
# 数据目录由容器内多个 UID 持有。借用已安装镜像的 tar，只读取实例。
docker run --rm --network none --read-only --user 0:0 \
    --mount "type=bind,source=$instance,target=/backup-source,readonly" \
    --entrypoint tar "$current_image_id" --numeric-owner -cf - -C /backup-source -- "${backup_files[@]}" \
    > "$archive.partial"
tar -tf "$archive.partial" >/dev/null
mv -- "$archive.partial" "$archive"
printf '升级前的成套备份：%s\n' "$archive"

print_recovery() {
    local active=docker-compose.yaml failed="$backup/failed-instance" entry
    [[ -f $active ]] || active=$current
    printf '更新失败，升级前备份：%s\n' "$archive"
    printf '请先在实例目录停止容器并确认没有运行中的容器：\n'
    printf 'cd -- %q\n' "$instance"
    printf 'docker compose -p %q -f %q stop openerp\n' "$project" "$active"
    printf 'docker compose -p %q -f %q ps --all\n' "$project" "$active"
    printf '随后以有权限还原数值属主的账号执行以下命令，保留故障现场并成套恢复：\n'
    printf '(\n    set -e\n    cd -- %q\n    mkdir -m 0700 -- %q\n' "$instance" "$failed"
    for entry in data .env compose.yaml docker-compose.yaml; do
        if [[ -e $entry || -L $entry ]]; then
            printf '    mv -- %q %q/\n' "$entry" "$failed"
        fi
    done
    printf '    tar --numeric-owner --same-owner -xpf %q\n' "$archive"
    printf '    docker compose -p %q -f %q up --detach --pull never --no-build\n)\n' "$project" "$current"
}

phase=apply
if ! mv -f -- "$work/docker-compose.yaml" docker-compose.yaml; then
    phase=stopping
    fail '无法替换实例的 Compose 文件。'
fi
if [[ $current == compose.yaml ]]; then
    mv -- compose.yaml "$backup/compose.yaml"
fi
compose docker-compose.yaml up --detach --force-recreate --pull never --no-build
new_id=$(container_id docker-compose.yaml)
jq -e --arg image "$expected_image" --arg id "$expected_id" '
    .State.Status == "running" and .Config.Image == $image and .Image == $id
' <<< "$(inspect "$new_id")" >/dev/null || fail '运行容器的镜像与本次拉取的 Release 镜像不匹配。'
printf '实例已运行镜像 %s（%s）。\n' "$expected_image" "$expected_id"
