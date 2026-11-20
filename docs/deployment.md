# 部署说明

OpenERP 以一个镜像、一个容器运行在 Linux amd64 服务器上。服务器需要 Docker Engine 28 或更高版本、Docker Compose v2 或更高版本、能从发布附件指定的地址拉取镜像，以及负责 HTTPS 的外层反向代理。[Docker 回环端口绑定](https://docs.docker.com/engine/network/port-publishing/#publishing-ports)

## 首次安装

等 [GitHub Release](https://github.com/wxwerp-org/openerp/releases) 的 `publish` job 上传部署附件后，从同一 Release 下载 **`docker-compose.yaml`**，放入该实例的长期目录，例如 `/opt/openerp/`。在该目录执行：

```bash
docker compose up -d
```

Compose 拉取附件指定版本的镜像并启动 `openerp`。首次启动自动在同目录的 `data/` 创建数据和凭据。入口依次完成 PostgreSQL 建库、EF 迁移、Redis 和对象存储启动，再开放 API 和 Web。后台启动命令返回后，可通过 `docker compose logs -f openerp` 查看启动进度，访问 `/api/auth/status` 检查应用是否可用。

同一台服务器部署第二套时，选择不同名称的空目录，为它设置不同的 `WEB_PORT`（例如在该实例目录的 `.env` 写入 `WEB_PORT=8081`），再执行同一命令。Compose 默认按目录名区分项目，目录下的数据和凭据各自独立。

## HTTPS 入口与首次登录

将外层 HTTPS 反代（例如 Caddy）的整个站点转发到 `http://127.0.0.1:8080`，保留原请求路径和 Host，并覆盖客户端可能发送的 `X-Forwarded-Proto` 和 `X-Forwarded-For`，分别写入 `https` 和外层反代所见的客户端地址。Caddy 默认即按此设置转发头并透传 Host，最小配置为：

```caddyfile
example.com {
	encode gzip
	reverse_proxy 127.0.0.1:8080
}
```

改动 `WEB_PORT` 后同步修改反代上游。外层代理负责 TLS、HTTP→HTTPS 与压缩，容器内的 Caddy 处理 SPA 深链接和同源 `/api/*`。外层代理应允许至少 21 MiB 的上传请求体。若它前面还有 CDN 或负载均衡，需在外层配置可信代理，以保留真实客户端地址。

开放入口前，通过 HTTPS 打开首页并检查 `/api/auth/status`，确认 `/api/auth/csrf` 返回 200 且设置带 `Secure` 的 Cookie。在「设置管理员密码」页设置密码，再用 `admin` 登录。密码长度为 6–20 个字符，保留首尾空白。

登录后直达并刷新 `/shops`、`/reports/orders` 与 `/settings`，确认未知页面显示“页面不存在”，缺失静态资源返回 404，使用有效访问令牌请求 `/api/not-a-route` 也返回 404。验证文件可以上传和下载。需要使用 ERP 功能时，通过右上角「系统设置 → 基础 → ERP 接入」填写 Token，到授权店铺页验证同步，再从订单列表工具栏打开「同步历史订单」查看进度和结果。

初始化入口匿名开放。公网可达时，第一个成功设置密码的人将成为管理员，因此应用启动后应立即初始化。全新实例被抢先初始化且尚无业务数据时可重新部署。已有数据时可通过[重置管理员密码](#重置管理员密码)恢复，保留数据库和数据卷。

## 停机、备份与恢复

在实例目录执行 `docker compose stop`。入口先关闭 Web 和 API，再等待 PostgreSQL、Redis 和 SeaweedFS 完成正常关闭。Compose 为整套服务预留两分钟。若进程被强制终止，请先启动并确认正常，重新干净停机后再归档数据库文件。[PostgreSQL 文件系统备份](https://www.postgresql.org/docs/18/backup-file.html)

停机后一起备份整个 `data/` 和当时使用的 `docker-compose.yaml`，保留文件属主和权限。数据目录包含 PostgreSQL、Redis 快照、SeaweedFS 文件和元数据，以及每个实例独有的数据库和 S3 凭据。按含有密钥的备份保管。恢复时将两者还原到同一实例目录，再运行 `docker compose up -d`。已有数据缺少对应凭据时，入口会报出缺失的位置，请恢复与该数据匹配的凭据。

需要单独核对凭据时，有相应宿主机权限的运维人员可在实例目录查看 `data/credentials/postgres/current/password` 和 `data/credentials/s3/current/s3.json`。这些文件包含真实密钥。

## 容器重建与升级

如果原实例使用旧附件 `compose.yaml`，请在停机备份时将该文件移出实例目录。两个文件共存时，Compose 会优先读取旧文件。

在维护窗口备份后，将新版 Release 的 `docker-compose.yaml` **覆盖到原实例目录**，保留整个 `data/`，然后运行：

```bash
docker compose up -d
```

新容器复用原数据和凭据，执行待应用的 EF 迁移。登录原账号，确认业务记录和已有附件均可读取。若迁移失败，Web 不会开放，使用 `docker compose logs openerp` 查看失败阶段。数据库结构已经改变且旧版不兼容时，停机后从升级前的备份成套恢复。

运行中任一常驻进程意外退出会触发整套容器重启，期间 ERP 短暂不可用。手动执行 `docker compose stop` 后保持停止。本部署方式从空目录开始，面向此后单容器版本之间的升级。

## houqin staging 自动部署

每次发布 GitHub Release（含预发布），发布工作流成功推送镜像并上传 `docker-compose.yaml` 后，会通过 SSH 更新 `openerp.wxwerp.com` 的 `/opt/openerp/`。其他实例仍可从同一 Release 下载附件，按上文手动部署。houqin 的部署结果可在该 Release 对应的 Actions 运行中查看，job 名为「部署 houqin staging」。

### 一次性配置

1. 确认单容器实例已按 `/opt/openerp/docker-compose.yaml` 运行，沿用该目录的端口配置、`data/` 和已有 HTTPS 反代。部署账号需要能在实例目录写入文件并操作 Docker。服务器需要 Bash、Docker Compose、`jq`、`flock` 和 `tar`。
2. 为 houqin 准备一对专用 SSH 密钥，将公钥加入部署账号的 `authorized_keys`。通过服务器控制台或已验证的运维通道取得 SSH 主机公钥。
3. 在 GitHub 仓库 **Settings → Secrets and variables → Actions** 中配置下表。私钥使用完整 OpenSSH 私钥文本，部署时由临时 runner 读取。[GitHub Actions secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets) · [GitHub Actions variables](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-variables)

| 名称 | GitHub 存储位置 | 值 |
| --- | --- | --- |
| `HOUQIN_SSH_PRIVATE_KEY` | Secrets | 与部署账号公钥匹配、可供非交互使用的专用私钥 |
| `HOUQIN_SSH_USER` | Variables | SSH 部署账号 |
| `HOUQIN_KNOWN_HOSTS` | Variables | 已核验的主机公钥行，例如 `openerp.wxwerp.com ssh-ed25519 <主机公钥>` |
| `HOUQIN_SSH_PORT` | Variables，可选 | 默认 `22`。自定义端口时，公钥行的主机部分写作 `[openerp.wxwerp.com]:端口` |

### 升级和故障处理

部署先核对 Release 附件的 SHA-256。服务器核对现有实例、端口与挂载，拉取目标镜像并核对 GHCR 发布摘要。上述检查通过后，才安装同一份 `docker-compose.yaml` 并运行 `docker compose up -d`。新容器沿用原数据和凭据，按镜像入口执行迁移并启动服务。最后，工作流核对实际运行镜像，从 GitHub runner 通过公网 HTTPS 检查 `/api/auth/status`。

连接或镜像拉取失败时，原实例继续运行。修正配置后，在该次运行选择 **Re-run failed jobs**，或执行 `gh run rerun <run-id> --failed`。已经部署的同一次运行重试时会再次核对实例；较早的运行不能覆盖已接管的新版本。[GitHub 重试说明](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)

若切换后容器未能启动，或公网 HTTPS 检查超时，可在实例目录执行 `docker compose ps -a` 和 `docker compose logs --tail=100 openerp` 查看启动阶段。数据库迁移先于 API 和 Web 启动。公网验收要求接口返回 HTTP 200，且 `initialized` 为布尔值。

## 重置管理员密码

在正在运行的实例目录执行：

```bash
docker compose exec openerp dotnet /app/OpenERP.Api.dll reset-admin-password
```

命令更新管理员密码并撤销已有登录。未初始化的账户会在重置时完成初始化。交互模式隐藏输入并要求确认。自动化时使用 `docker compose exec -T`，为子命令追加 `--stdin`，通过标准输入传入一行密码。失败时命令返回非零退出码。
