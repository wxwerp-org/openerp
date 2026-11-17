# 部署说明

OpenERP 的部署目标为单台 Linux amd64 服务器。服务器需要 Docker Engine 28 或更高版本、支持 `up --wait` 的 Docker Compose v2 或更高版本，以及负责 HTTPS 的反向代理。Web 端口依赖 Docker [回环绑定的隔离保证](https://docs.docker.com/engine/network/port-publishing/#publishing-ports)。开发启动见 [README](../README.md#快速开始)。

## 获取产物

发布 [GitHub Release](https://github.com/wxwerp-org/openerp/releases) 会触发发布工作流。等工作流成功后，从同一 Release 下载 `openerp-compose.tar.gz` 并解压到该版本的独立目录。附件中的 Compose 已引用该 Release 标签对应的 API、Web 和迁移镜像。部署时只需拉取镜像。

若 GHCR 包为私有，用有包读取权限的 GitHub 账号登录。将具备 `read:packages` 权限的 classic PAT 通过标准输入传给 `docker login ghcr.io -u 用户名 --password-stdin`。组织要求 SSO 时，给该 PAT 授权。

## 运行配置

在解压目录中填写 `.env`，设为仅部署用户可读写（`chmod 600 .env`）。

| 变量 | 设置 |
| --- | --- |
| `POSTGRES_PASSWORD`、`REDIS_PASSWORD` | 为每项分别生成随机值，例如 `openssl rand -hex 24`。 |
| `SEAWEEDFS_ACCESSKEY`、`SEAWEEDFS_SECRETKEY` | 分别生成随机十六进制值。 |
| `JWT_SIGNING_PRIVATE_KEY_PEM` | 至少 2048 位 RSA 私钥。用单引号包住完整的多行 PEM，例如第一行写 `JWT_SIGNING_PRIVATE_KEY_PEM='-----BEGIN PRIVATE KEY-----`，在末行的 `-----END PRIVATE KEY-----` 后关闭单引号。 |
| `WXW_ERP_ACCESS_TOKEN` | 私有 ERP Token。留空可启动，访问私有 ERP 时需要有效 Token。 |
| `WEB_PORT` | 默认 8080。需要改端口时在 `.env` 追加该项，并同步修改外层反代的上游端口。 |

保管好运行配置和私钥。升级和替换容器时复用同一组数据库、缓存、存储凭据及签名私钥。签名私钥支持 PKCS#8 或 PKCS#1，可用 `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048` 生成。

## 首次部署

在解压目录运行以下命令。始终使用同一 Compose 项目名 `openerp`，以便切换 Release 目录时复用数据卷。

```bash
docker compose -p openerp config --quiet
docker compose -p openerp pull
docker compose -p openerp up -d --wait
docker compose -p openerp ps -a
```

PostgreSQL 就绪后，`migrations` 执行同版本 EF Core bundle。它成功退出后，Compose 才启动 API。确认 `migrations` 显示 `Exited (0)`，其余服务正在运行。若迁移失败，从 `docker compose -p openerp logs migrations` 查原因，修复后重新执行 `up`。若 API 启动失败，检查 `docker compose -p openerp logs api` 中的签名私钥、数据库及对象存储错误。

将外层 HTTPS 反代的整个站点转发到 `http://127.0.0.1:8080`，保留原请求路径和 Host，并覆盖客户端可能发送的 `X-Forwarded-Proto` 和 `X-Forwarded-For`，分别写入 `https` 和外层反代所见的客户端地址。若反代容器已接入 `openerp_aspire` 网络，可用 `http://web:5000` 作为上游。YARP 和 API 接受转发头，Web 的宿主机端口须保持仅绑定回环地址，API 端口须留在 Compose 网络内，该网络仅接入受信任的部署容器。外层反代应允许上传功能所需的请求体大小（至少 21 MiB，含 multipart 开销）。

开放入口前，通过 HTTPS 检查首页、SPA 深链接和 `/api/auth/status`，确认 `/api/auth/csrf` 返回 200 且设置带 `Secure` 的 Cookie。在「设置管理员密码」页设置并确认密码，再用 `admin` 登录。密码长度为 6–20 个字符，保留首尾空白。登录后验证文件上传和下载；有 ERP 凭证时，在店铺页验证配置和订单同步。

初始化入口匿名开放。公网可达时，第一个成功设置密码的人将成为管理员，因此应用启动后应立即初始化。全新实例被抢先初始化且尚无业务数据时可重新部署。已有数据时可通过[重置管理员密码](#重置管理员密码)恢复，保留数据库和数据卷。

## 版本升级

1. 从新 Release 解压到新目录，将现有运行配置填入新版模板，按首次部署的命令校验配置并拉取镜像。
2. 进入维护窗口。在旧目录执行 `docker compose -p openerp stop web api`，停止旧 API 写入。备份 PostgreSQL 和对象存储，保留旧 Release 附件及运行配置。
3. 在新目录执行 `docker compose -p openerp up -d --wait`。沿用项目名和数据卷，新版本的迁移容器会应用尚未执行的迁移。检查迁移退出码，再验证 HTTPS、登录和已有数据。
4. 迁移失败时保持维护窗口，检查迁移日志和实际数据库状态，修复后重试。确认旧版 API 与当前数据库架构兼容后才可切回旧镜像。若架构不兼容，按备份恢复数据后再启动旧版本。

替换或停止容器会保留命名卷。升级和容器替换时避免用 `down -v`，它会删除 PostgreSQL、Redis 和 SeaweedFS 的数据卷。Redis 使用默认快照持久化，非正常退出时最后一次快照之后的写入可能丢失。

常驻容器使用 `unless-stopped`，在异常退出或 Docker 重启后自动恢复，手动停止的容器保持停止。一次性迁移容器由 Compose 的启动流程管理，Docker 重启时不会自动重跑迁移。

从 Cookie 认证版本升级到 JWT 时，迁移创建 `RefreshTokens` 表并删除 `AdminSessions` 表，保留管理员密码、资料和最近登录时间。旧版登录会话全部失效，各设备须重新加载页面并使用原密码登录。

## 重置管理员密码

到正在运行的部署目录执行以下命令。Compose 会复用 API 容器内的数据库连接配置。

```bash
docker compose -p openerp exec api dotnet OpenERP.Api.dll reset-admin-password
```

命令仅连接目标数据库，更新已有管理员并撤销全部登录。未初始化的占位账户会在重置时完成初始化。签名私钥只用于 HTTP 服务，重置命令无需配置。交互模式隐藏输入并要求确认。自动化时追加 `--stdin`，由机密管理工具通过标准输入提供一行密码，避免把真实密码写进脚本或终端历史。失败时命令返回非零退出码。
