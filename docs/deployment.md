# 部署说明

OpenERP 的部署目标为单台 Linux amd64 服务器。服务器需要 Docker Engine 28 或更高版本、支持 `up --wait` 的 Docker Compose v2 或更高版本，以及负责 HTTPS 的反向代理。Web 端口依赖 Docker [回环绑定的隔离保证](https://docs.docker.com/engine/network/port-publishing/#publishing-ports)。开发启动见 [README](../README.md#快速开始)。

## 获取产物

发布 [GitHub Release](https://github.com/wxwerp-org/openerp/releases) 会触发发布工作流。等工作流成功后，从同一 Release 下载 `openerp-compose.tar.gz` 并解压到该版本的独立目录。

## 首次部署

首次部署使用空的数据卷和凭据卷。在解压目录运行以下命令。默认 Compose 项目名为 `openerp`。需要调整回环端口时，可通过 `WEB_PORT` 覆盖默认值 8080，并同步修改反代上游端口。

```bash
docker compose up
```

首次启动时，`credentials` 生成 PostgreSQL 密码与 SeaweedFS 的 S3 凭据，并分别保存到两个 Docker 命名卷。Redis 在 Compose 内网使用无密码模式。PostgreSQL 就绪后，`migrations` 执行同版本 EF Core bundle，迁移成功后 API 才启动。用 `docker compose up -d --wait` 可在后台启动，用 `docker compose ps -a` 确认 `credentials` 和 `migrations` 已成功退出。启动失败时通过 `docker compose logs` 查看出错的服务，修复后重新执行 `up`。

将外层 HTTPS 反代的整个站点转发到 `http://127.0.0.1:8080`，保留原请求路径和 Host，并覆盖客户端可能发送的 `X-Forwarded-Proto` 和 `X-Forwarded-For`，分别写入 `https` 和外层反代所见的客户端地址。若反代容器已接入 `openerp_aspire` 网络，可用 `http://web:5000` 作为上游。YARP 和 API 接受转发头，Web 的宿主机端口须保持仅绑定回环地址，API 端口须留在 Compose 网络内，该网络仅接入受信任的部署容器。外层反代应允许上传功能所需的请求体大小（至少 21 MiB，含 multipart 开销）。

开放入口前，通过 HTTPS 打开首页并检查 `/api/auth/status`，确认 `/api/auth/csrf` 返回 200 且设置带 `Secure` 的 Cookie。在「设置管理员密码」页设置并确认密码，再用 `admin` 登录。密码长度为 6–20 个字符，保留首尾空白。登录后验证文件上传和下载。需要使用 ERP 功能时，通过右上角「系统设置 → 基础 → ERP 接入」填写 Token，再到店铺页验证查询和订单同步。当前 Release 直接打开 `/settings` 会返回 404，请从首页进入系统设置。

初始化入口匿名开放。公网可达时，第一个成功设置密码的人将成为管理员，因此应用启动后应立即初始化。全新实例被抢先初始化且尚无业务数据时可重新部署。已有数据时可通过[重置管理员密码](#重置管理员密码)恢复，保留数据库和数据卷。

## 凭据查找与备份

用 `docker volume ls --filter label=com.docker.compose.project=openerp` 查找部署卷。默认的凭据卷名是 `openerp_postgres-credentials` 和 `openerp_s3-credentials`。有 Docker 权限的运维人员在部署目录中可按需读取凭据。以下命令直接输出凭据，只在受控终端使用。

```bash
docker compose exec postgres cat /run/openerp/postgres/current/password
docker compose exec seaweedfs cat /run/openerp/s3/current/s3.json
```

服务停机时，先用 `docker volume inspect` 确认凭据卷存在，再从部署目录运行以下命令。临时容器复用附件指定的本地 API 镜像和只读挂载。

```bash
docker compose run --rm --no-deps --pull never --entrypoint cat api /run/openerp/postgres/current/password
docker compose run --rm --no-deps --pull never --entrypoint cat api /run/openerp/s3/current/s3.json
```

用 [Docker 卷的备份和恢复流程](https://docs.docker.com/engine/storage/volumes/#back-up-restore-or-migrate-data-volumes)归档或恢复卷前，先在维护窗口停止 Web 和 API，再停止 PostgreSQL 和 SeaweedFS。PostgreSQL 的普通文件归档要求数据库干净停止，见 [PostgreSQL 文件系统备份说明](https://www.postgresql.org/docs/18/backup-file.html)。将 `postgres-credentials` 和 `postgres-data` 成组备份和恢复，将 `s3-credentials` 和 `seaweedfs-data` 成组备份和恢复。凭据归档存入加密备份。恢复的 PostgreSQL 数据与原角色密码配套。

## 容器重建与版本升级

保留 Compose 项目名及其命名卷，重新执行 `docker compose up` 即可复用已生成的凭据和数据。`docker compose down -v` 会删除数据卷和凭据卷，仅在要移除整套实例时使用。Redis 使用快照持久化，异常退出时最后一次快照之后的写入可能丢失。

使用自动生成凭据的实例更换 Release 时，在维护窗口停止旧服务并按上文备份卷。在新目录沿用项目名及命名卷，运行 `docker compose up -d --wait`，检查迁移退出码，再验证 HTTPS、登录和已有数据。迁移失败时保持维护窗口，查看迁移日志和数据库状态，修复后重试。确认旧版 API 与当前数据库架构兼容后才可切回旧镜像；架构不兼容时先从备份恢复。

常驻容器使用 `unless-stopped`，在异常退出或 Docker 重启后自动恢复，手动停止的容器保持停止。Docker 重启时不会自动重跑凭据初始化和迁移容器。API 每次启动生成临时 JWT 签名密钥，重启前的 access token 随即失效，浏览器仍可用有效的 refresh Cookie 续期。重启时遇到失败的保存或上传，可以在会话续期后重新提交。

## 重置管理员密码

到正在运行的部署目录执行以下命令。

```bash
docker compose exec api dotnet OpenERP.Api.dll reset-admin-password
```

命令仅使用 API 容器内的 PostgreSQL 配置连接数据库，更新已有管理员并撤销全部登录。未初始化的占位账户会在重置时完成初始化。交互模式隐藏输入并要求确认。自动化时追加 `--stdin`，由机密管理工具通过标准输入提供一行密码，避免把真实密码写进脚本或终端历史。失败时命令返回非零退出码。
