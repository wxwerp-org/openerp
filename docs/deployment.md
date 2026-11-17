# 单容器 Production 部署

Production 通过 Aspire 发布 Docker Compose 配置，镜像中将 API、PostgreSQL、Redis 和 SeaweedFS 放入同一个 `openerp` 服务。Production 不启用 Aspire Dashboard。当前镜像仅构建 `linux/amd64`，PostgreSQL 主版本为 18；在 arm64 构建机上得到 amd64 镜像不代表镜像支持或已在 arm64 上原生验收。容器 CI 的 x64 构建与生命周期验收定义见 [ci-container.yml](../.github/workflows/ci-container.yml)，实际运行结果以对应 GitHub Actions 记录为准。

## 发布 Compose 并构建镜像

在仓库根目录执行。仓库的 `aspire.config.json` 已指定 AppHost；`aspire publish` 生成 Compose 文件与参数占位文件，但不构建应用镜像：

```bash
aspire publish --output-path ./aspire-output --non-interactive
docker buildx build \
  --platform linux/amd64 \
  --load \
  --tag openerp:release1 \
  --file deploy/container/Dockerfile \
  .
```

将 `aspire-output/docker-compose.yaml` 和 `.env` 复制到部署目录。编辑 `.env`，按部署环境填写：

| 变量 | 示例 / 说明 |
| --- | --- |
| `OPENERP_IMAGE` | `openerp:release1`，须与构建或加载的镜像标签一致。 |
| `ALLOWED_HOSTS` | `erp.example.com`；多个主机名用分号分隔，不含协议或端口，不可设为 `*`。 |
| `TRUSTED_PROXY` | `172.30.0.10`；反向代理连接到 API 时的真实来源 IP，只接受单个 IP，不可填 CIDR。 |
| `OPENERP_BINDMOUNT_0` | 密钥目录在 Docker 主机上的绝对路径，默认 `/etc/openerp/secrets`。 |
| `OPENERP_BIND_ADDRESS` | 默认 `127.0.0.1`，适合宿主机反向代理。 |
| `OPENERP_HTTP_PORT` | 默认 `8081`，宿主机端口。 |
| `OPENERP_APPLY_MIGRATIONS` | 默认 `0`；仅备份后显式升级时临时设为 `1`。 |

在 arm64 构建机上，BuildKit 需要能构建 amd64 镜像；部署目标仍是 amd64。`--load` 将镜像导入当前 Docker Engine。若使用另一台 Docker 主机，传输镜像和 Compose 配置：

```bash
docker image save --output ./openerp-release1.tar openerp:release1
```

在目标主机执行 `docker image load --input ./openerp-release1.tar`。不要把密钥内容放入 `.env`、Compose 文件、镜像或命令行。

## 密钥、TLS 与主机校验

Production Compose 将密钥目录只读挂载到容器 `/run/secrets`。在 Docker 主机首次部署时创建这些单行 UTF-8 文件：

| 文件 | 说明 |
| --- | --- |
| `postgres-password` | 必需、非空；初始化 PostgreSQL `openerp` 用户。必须与现有数据库密码保持一致。 |
| `redis-password` | 必需、非空；配置 Redis 认证。 |
| `s3-access-key`、`s3-secret-key` | 必需、非空；配置 SeaweedFS S3 身份及 API 凭证。 |
| `admin-initial-password` | 首次创建管理员时必需，6–20 个字符。首次登录后在 Web 改密；已有管理员时不会覆盖数据库密码。 |
| `erp-access-token` | 可选；配置后可查询店铺并同步订单。 |

可用下列命令在尚未配置任何密钥的主机生成初始随机值。若目标文件已存在，命令会停止，不会覆盖数据库凭证：

```bash
set -eu
secret_dir=/etc/openerp/secrets
install -d -m 0700 "$secret_dir"
for name in postgres-password redis-password s3-access-key s3-secret-key admin-initial-password; do
  if [ -e "$secret_dir/$name" ]; then
    printf '文件已存在，拒绝覆盖：%s\n' "$secret_dir/$name" >&2
    exit 1
  fi
done
umask 077
for name in postgres-password redis-password s3-access-key s3-secret-key; do
  openssl rand -hex 32 > "$secret_dir/$name"
done
openssl rand -hex 8 > "$secret_dir/admin-initial-password"
chmod 0600 "$secret_dir"/*
```

`admin-initial-password` 为 16 个十六进制字符，符合长度要求。需要 ERP 时，由密钥管理工具写入 `erp-access-token`。密钥内容不得有内部换行或 NUL，最多允许一个末尾换行。特别是不可只替换现有数据卷对应的 `postgres-password`：PostgreSQL 只在初始化空数据目录时读取它，改文件不会改变既有数据库用户密码。默认宿主机目录 `/etc/openerp/secrets` 可在 `.env` 的 `OPENERP_BINDMOUNT_0` 中替换；容器内挂载点 `/run/secrets` 不变。

容器只提供 HTTP，端口目标为 `8081`；默认只发布到 Docker 主机 `127.0.0.1:8081`。由反向代理终止外部 TLS，保留原始 `Host` 并设置 `X-Forwarded-Proto: https`。代理须先清理外部传入的转发头，再写入可信值。`TRUSTED_PROXY` 必须是 API 实际看到的代理来源 IP；`AllowedHosts` 与可信代理在 Production 均为必需。不要把容器 HTTP 端口直接暴露到公网。

## 首次启动与日常操作

在部署目录设置同一个 Compose 命令函数，后续操作都使用它：

```bash
dc() { docker compose --env-file .env -f docker-compose.yaml "$@"; }
dc config --quiet
dc up -d --no-build --pull never openerp
dc ps
dc logs --tail=200 openerp
```

在同一个 Bash 会话中继续后续命令；新会话需重新定义 `dc`。确认服务健康、域名经 HTTPS 代理可访问后再开放用户流量。首次使用空数据卷时，容器会建立数据库结构并创建管理员。首次登录使用 `admin-initial-password`，随后按页面要求修改密码。忘记密码时使用本页[管理员密码重置](#管理员密码重置)。健康端点 `/health` 和 `/alive` 匿名可用，只返回总体状态。

Compose 为以下数据路径建立独立命名卷：

| 容器路径 | 数据 |
| --- | --- |
| `/var/lib/postgresql` | PostgreSQL 18 集群与业务数据库；当前 `PGDATA` 为 `/var/lib/postgresql/18/docker`。 |
| `/var/lib/redis` | Redis 快照数据。 |
| `/data` | SeaweedFS Master、Volume、Filer 与 S3 对象数据。 |

普通停止与服务重建会保留卷。不要对生产部署使用 `docker compose down --volumes` 或 `aspire destroy` 做普通停机，它们会删除持久卷。

s6 先启动并检查基础服务，再执行独立迁移阶段，最后启动 API。正常停机先停 API，再停依赖；PostgreSQL 收到 fast shutdown 信号，Redis 保存快照后退出。任一关键进程意外退出时，整个容器有序非零退出，Compose 使用 `on-failure:3` 重试；API 故障也会连带重启数据库。仅健康检查失败会标记 unhealthy，不自动重启。日志按组件标识写入标准输出。

所有组件随镜像一起升级，应用更新也需要停机。Redis 继续使用快照持久化，异常终止可能丢失最近一次快照后的缓存写入。订单同步任务状态仍在内存中，重启后不续跑；已入库订单保留。版本升级需同时检查镜像中各组件及动态库的兼容性，并重复恢复演练。

## 备份与应用迁移

更新前进入维护窗口，先停止经代理的新请求，关闭定时同步并等待正在运行的同步任务完成。在线导出逻辑数据库，然后有序停止容器并归档三个数据卷、旧镜像及配置。以下命令需要在仍运行的旧版本容器上执行；`BACKUP_DIR` 是宿主机绝对路径：

```bash
BACKUP_DIR="/var/backups/openerp/$(date +%Y%m%d-%H%M%S)"
umask 077
mkdir -p "$BACKUP_DIR"

dc exec -T openerp sh -ec '
  export PGPASSWORD="$(cat /run/openerp/postgres-password)"
  exec pg_dump --host=127.0.0.1 --username=openerp --dbname=openerp --format=custom
' > "$BACKUP_DIR/openerp.dump"

CONTAINER_ID=$(dc ps --all --quiet openerp)
IMAGE_ID=$(dc images --quiet openerp | head -n 1)
test -n "$CONTAINER_ID"
test -n "$IMAGE_ID"
ROLLBACK_IMAGE="openerp:rollback-$(date +%Y%m%d-%H%M%S)"
docker image tag "$IMAGE_ID" "$ROLLBACK_IMAGE"
docker image save --output "$BACKUP_DIR/openerp-image.tar" "$ROLLBACK_IMAGE"

dc stop --timeout 180 openerp
docker run --rm --entrypoint /bin/sh --volumes-from "${CONTAINER_ID}:ro" \
  --mount "type=bind,source=$BACKUP_DIR,target=/backup" "$IMAGE_ID" -ec '
    tar --numeric-owner -C /var/lib/postgresql -cpf /backup/postgresql.tar .
    tar --numeric-owner -C /var/lib/redis -cpf /backup/redis.tar .
    tar --numeric-owner -C /data -cpf /backup/seaweedfs.tar .
  '
```

`openerp.dump` 用于 PostgreSQL 跨主版本的逻辑恢复；卷归档用于同一 PostgreSQL 主版本的完整物理恢复，并保留 Redis 和 SeaweedFS 数据。卷归档必须在服务停止后制作。加密并异地保存数据库转储、卷归档、密钥目录、旧镜像、旧 `docker-compose.yaml` 和 `.env`；这些备份应一起验证，恢复后再开放流量。

Production 容器启动时会检查 EF Core 迁移历史和预期表/列集合：

- 空数据库自动执行镜像中的迁移；已匹配当前镜像的数据库直接启动。
- 可识别的待应用迁移默认拒绝启动。完成并验证备份后，才将 `.env` 的 `OPENERP_APPLY_MIGRATIONS` 临时设为 `1` 并重建服务。
- 未知、乱序或结构不匹配的迁移历史始终拒绝启动；设置 `1` 不能绕过兼容性检查。

该启动检查不是完整数据库审计：它比较迁移记录，以及应用实体对应的表和列集合；不会全面比较列类型、索引或约束。非容器方式直接运行 Production API 仍由运维先应用迁移。

应用版本更新时使用上一节的备份，更新 `.env` 的 `OPENERP_IMAGE`，必要时仅在迁移窗口将 `OPENERP_APPLY_MIGRATIONS=1`，然后重建：

```bash
dc up -d --no-build --pull never --force-recreate openerp
dc logs --tail=200 openerp
```

确认日志与服务健康、业务数据可用后，将迁移变量恢复为 `0` 并再次强制重建；再恢复代理流量。遇到 `Migrations pending` 先检查备份与目标镜像，不要反复授权。遇到不兼容迁移历史时，不要手工改写 `__EFMigrationsHistory`；按匹配版本恢复。

## PostgreSQL 主版本升级

PostgreSQL 数据目录不能直接跨主版本共用。官方 entrypoint 会检测 `/var/lib/postgresql/*/docker` 中其他主版本的 `PG_VERSION` 并拒绝启动；不会自动转换数据。当前镜像固定使用 PostgreSQL 18。升级主版本前先用旧版本 `pg_dump --format=custom` 逻辑导出，并完整保留旧镜像、旧 Compose 配置和旧的三个卷；不要把旧 PostgreSQL 物理归档放进新主版本的数据卷。

新版本使用从未使用过的 Compose 项目名来创建一组新卷。下面示例假定 `.env` 与 Compose 文件已更新到新镜像：

```bash
NEW_PROJECT="openerp-pg18-$(date +%Y%m%d%H%M%S)"
dc --project-name "$NEW_PROJECT" create openerp
NEW_CONTAINER_ID=$(dc --project-name "$NEW_PROJECT" ps --all --quiet openerp)
NEW_IMAGE_ID=$(dc --project-name "$NEW_PROJECT" images --quiet openerp | head -n 1)
test -n "$NEW_CONTAINER_ID"
test -n "$NEW_IMAGE_ID"
```

先把旧 Redis 与 SeaweedFS 归档恢复到新项目的对应卷；不要恢复 `postgresql.tar`：

```bash
docker run --rm --entrypoint /bin/sh --volumes-from "$NEW_CONTAINER_ID" \
  --mount "type=bind,source=$BACKUP_DIR,target=/backup,readonly" "$NEW_IMAGE_ID" -ec '
    tar --numeric-owner --same-owner -C /var/lib/redis -xpf /backup/redis.tar
    tar --numeric-owner --same-owner -C /data -xpf /backup/seaweedfs.tar
  '
```

保持反向代理维护状态；新空库首次启动仍需提供临时管理员初始密码文件，该临时账户会随稍后的新库重建被替换。启动新项目并等待容器健康。暂停 API，再把逻辑转储恢复到全新数据库：

```bash
dc --project-name "$NEW_PROJECT" up -d --no-build --pull never openerp
dc --project-name "$NEW_PROJECT" exec -T openerp /command/s6-svc -d /run/service/api
dc --project-name "$NEW_PROJECT" exec -T openerp /command/s6-svwait -d -t 45000 /run/service/api
dc --project-name "$NEW_PROJECT" cp "$BACKUP_DIR/openerp.dump" openerp:/tmp/openerp.dump
dc --project-name "$NEW_PROJECT" exec -T openerp sh -ec '
  export PGPASSWORD="$(cat /run/openerp/postgres-password)"
  psql --set=ON_ERROR_STOP=1 --host=127.0.0.1 --username=openerp --dbname=postgres \
    --command="DROP DATABASE openerp WITH (FORCE)"
  createdb --maintenance-db=postgres --host=127.0.0.1 --username=openerp --owner=openerp openerp
  pg_restore --single-transaction --no-owner --role=openerp \
    --host=127.0.0.1 --username=openerp --dbname=openerp /tmp/openerp.dump
  rm /tmp/openerp.dump
'
```

然后在新 `.env` 中将 `OPENERP_APPLY_MIGRATIONS` 临时设为 `1`，强制重建新项目以升级导入的应用结构：

```bash
dc --project-name "$NEW_PROJECT" up -d --no-build --pull never --force-recreate openerp
dc --project-name "$NEW_PROJECT" logs --tail=200 openerp
```

确认健康、数据和登录/ERP/文件功能后恢复为 `0`，再次执行上述重建命令并开放流量。跨大版本逻辑恢复后，新数据目录只可由新 PG 主版本镜像打开。

## 回退与失败恢复

迁移后的新数据库不能只换回旧应用镜像：旧镜像可能不认识新迁移历史。回退使用升级前的旧镜像、旧 Compose 配置与完整卷快照，在独立 Compose 项目中恢复到全新卷；不要覆盖当前卷。示例中 `.env.old` 和 `docker-compose.old.yaml` 来自升级前备份；先加载旧镜像归档，并把 `.env.old` 的 `OPENERP_IMAGE` 设为备份时创建的 `openerp:rollback-...` 标签：

```bash
ROLLBACK_PROJECT="openerp-rollback-$(date +%Y%m%d%H%M%S)"
docker compose --project-name "$ROLLBACK_PROJECT" \
  --env-file .env.old -f docker-compose.old.yaml create openerp
RESTORE_ID=$(docker compose --project-name "$ROLLBACK_PROJECT" \
  --env-file .env.old -f docker-compose.old.yaml ps --all --quiet openerp)
RESTORE_IMAGE_ID=$(docker compose --project-name "$ROLLBACK_PROJECT" \
  --env-file .env.old -f docker-compose.old.yaml images --quiet openerp | head -n 1)
test -n "$RESTORE_ID"
test -n "$RESTORE_IMAGE_ID"
docker run --rm --entrypoint /bin/sh --volumes-from "$RESTORE_ID" \
  --mount "type=bind,source=$BACKUP_DIR,target=/backup,readonly" "$RESTORE_IMAGE_ID" -ec '
    tar --numeric-owner --same-owner -C /var/lib/postgresql -xpf /backup/postgresql.tar
    tar --numeric-owner --same-owner -C /var/lib/redis -xpf /backup/redis.tar
    tar --numeric-owner --same-owner -C /data -xpf /backup/seaweedfs.tar
  '
docker compose --project-name "$ROLLBACK_PROJECT" \
  --env-file .env.old -f docker-compose.old.yaml up -d --no-build --pull never openerp
```

物理 PostgreSQL 卷只能恢复给与其主版本相同的旧镜像。跨主版本回退使用升级前的旧物理卷和旧镜像；逻辑恢复到新 PG 主版本的数据不能交给旧 PG 镜像。回退到升级前快照会丢弃快照后的数据库、Redis 和对象写入。保留失败的新项目与卷以供排查，确认数据取舍后再清理。

启动或迁移失败时先检查状态和日志，保留现场与备份；不要因失败删除数据卷、手工改写迁移历史、重复授权迁移，或让不同 PostgreSQL 主版本打开同一 `PGDATA`：

```bash
dc ps --all
dc logs --tail=300 openerp
```

## 管理员密码重置

在运行中的 Production 容器内执行交互命令。输入会隐藏；命令只更新已有管理员密码，不启动第二个 API 或迁移数据库：

```bash
dc exec --user app -it openerp dotnet /app/OpenERP.Api.dll reset-admin-password
```

命令要求输入并确认一个 6–20 字符的新密码，成功后撤销全部登录会话。自动化应由 Secret 管理工具通过标准输入调用 `reset-admin-password --stdin`；不要把明文密码放入参数或 shell 历史。
