# OpenERP

Aspire 编排 Vue/Vite、ASP.NET Core Minimal API、PostgreSQL 和 Redis。后端只有一个应用进程；已接入私有 ERP 店铺读取，尚无订单本地保存。

## 私有 ERP 店铺（B2）

本期接入 `https://v3-api.wxwerp.com`，仅查询 Temu 半托管跨境（TemuPop）店铺。在仓库根目录设置 Open API Token：

```bash
dotnet user-secrets set "Parameters:wxw-erp-access-token" "你的Token" --project OpenERP.AppHost
```

按下文通过 Aspire 启动后打开 `/shops`。AppHost 将密钥参数注入后端的 `Erp__AccessToken`；前端只读取配置是否存在，不获取 Token，不把 Token 存入浏览器或数据库。修改 Token 后重新启动该 AppHost。直接配置后端时使用环境变量 `Erp__AccessToken`。

店铺页支持加载、重试和分页，逐店显示平台授权有效、无效或查询失败。Token 已配置不代表权限有效；能查询店铺也不代表平台授权有效。授权无效的店铺仍可显示，历史订单是否可读另行核验。当前没有管理员登录，部署及访问控制由 C1/C4 完成。

本轮确认 SID 30 用于历史订单联调，SID 644928 用于后续订单分页验证；B2 不触发订单同步或保存。调查记录和接口契约差异见 [B1 #6](https://github.com/wxwerp-org/openerp/issues/6)。

后端入口：`GET /api/erp/config`、`GET /api/erp/shops?page=1&pageSize=20`，每页 1–100 条。上游列表失败返回错误；单店授权状态查询失败保留店铺并标记未知。返回的页面数量和总数来自 ERP。

## 环境

- .NET SDK **10.0.404**（`global.json`）；本地 Aspire CLI **13.6.5**（工具清单）。
- Node.js **26.13.0**（`.node-version`）、pnpm **11.16.0**。
- 已启动的 Docker Desktop，或 Aspire 支持的兼容容器运行环境。
- 已受信任的 ASP.NET Core HTTPS 开发证书；浏览器日志需要 Chrome 或 Edge。

主要依赖：Vue 3.5.45、Vue Router 5.5.0、Element Plus 2.14.8、Vite 8.3.8、TypeScript 6.0.3、EF Core 10.0.15、Npgsql EF Provider 10.0.3。正式 Aspire 集成为 13.6.5；浏览器日志是唯一预览包例外：`Aspire.Hosting.Browsers 13.6.5-preview.1.26559.7`。

容器镜像按大版本固定为 `postgres:18`、`redis:8`，允许同一大版本内更新。2026-11-12 验证时分别为 PostgreSQL 18.6、Redis 8.10.3；未固定小版本或 digest，也不使用跨大版本的 `latest`。本机已有镜像可能被缓存，升级时应先拉取对应大版本 tag，再重启 AppHost。

## 启动

在仓库根目录执行首次准备：

```bash
dotnet tool restore
dotnet tool run aspire certs trust --non-interactive
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

证书已受信任时无需重复信任；首次操作可能出现系统钥匙串确认。

AppHost 的开发启动配置让 `localhost`、`127.0.0.1`、`::1` 绕过 HTTP 代理，避免 Dashboard 到 AppHost 的本地 gRPC 连接被系统代理转发；不会修改全局代理设置。

启动整套应用：

```bash
dotnet tool run aspire start --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

Aspire 会启动前端，无需另外执行 `pnpm dev`。通过命令输出的登录链接访问 Dashboard，在资源列表打开 `web` 的地址。端口动态分配，启动配置中的 `0` 表示自动分配，不是固定访问端口。

```bash
dotnet tool run aspire describe --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire wait web --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

## 验证与诊断

- 打开 `web`：Vue Router 渲染唯一的 `/` 页面，Element Plus 显示后端响应 `pong`；可点击重试。浏览器请求相对路径 `/api/ping`，Vite 将其转发到 Aspire 注入的 API 地址。开发代理仅为本地 HTTPS 后端接受自签证书。
- Dashboard 中检查 `postgres`、`openerp`、`redis`、`api`、`web` 的状态。API 的 `/health` 包含真实的 EF Core 数据库连接及 Redis 客户端检查；`/alive` 仅检查应用存活。
- Dashboard 中打开 API 的结构化日志与追踪，查看 `OpenERP connectivity check received`。浏览器日志通过 `web-browser-logs` 子资源查看。

```bash
dotnet tool run aspire otel logs api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire otel traces api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire resource web-browser-logs open-tracked-browser --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire logs web-browser-logs --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

资源名以 `aspire describe` 的实际输出为准。仓库保留已有 MCP 配置；agent 环境支持时可使用 Aspire MCP，否则这些官方 CLI 命令提供本地诊断路径。

独立构建，不要求 API 或容器正在运行：

```bash
dotnet build OpenERP.slnx --no-restore
pnpm --dir OpenERP.Web typecheck
pnpm --dir OpenERP.Web build
```

## 开发数据与停止

PostgreSQL 和 Redis 使用 Docker 命名卷及持久容器生命周期。卷由 Aspire 根据应用身份与资源名生成，保存在 Docker 管理的存储中；macOS Docker Desktop 下位于其 Linux 虚拟机内，不在源码目录。可在 Docker Desktop 的 Volumes 页面查看。

本 checkout 的卷为 `openerp.apphost-19ee1fd12e-postgres-data`（挂载到 `/var/lib/postgresql`）和 `openerp.apphost-19ee1fd12e-redis-data`（挂载到 `/data`）；其他 checkout 的身份后缀可能不同。

AppHost 首次运行自动生成开发密码并保存在 .NET User Secrets 中，不写入 Git。使用以下命令查看当前开发密钥文件的位置，避免打印秘密值：

```bash
dotnet tool run aspire secret path --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

正常停止：

```bash
dotnet tool run aspire stop --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

正常停止 AppHost 后，持久数据库容器和数据卷会保留，可在下次启动时复用。不要随意重置 UserSecretsId 或删除开发密钥，否则保留的数据卷可能与新密码不匹配。删除数据卷会丢失开发数据；`aspire stop --force` 不是普通停止命令。

Redis 使用官方数据卷集成启用的快照持久化；尚无缓存业务，也不承诺异常断电时保存最近的内存写入。

本次仅用于开发。前端生产镜像及 Caddy、后端生产镜像和部署流水线均未实现。

## 测试框架

- 后端：`OpenERP.Api.Tests` 使用 xUnit v3 + 原生 .NET 10 Microsoft.Testing.Platform，替换原 xUnit v2/VSTest；已引入与 AppHost 版本一致的 `Aspire.Hosting.Testing`。
- 前端：Vitest + Vue Test Utils + jsdom，测试目录为 `OpenERP.Web/tests/unit`。
- 浏览器：TypeScript + Playwright Test，测试目录为 `OpenERP.Web/tests/e2e`，使用 Chromium。

本次仅接入框架并保留已有文件存储测试，不新增用例或测试环境管理。前端两个测试目录目前没有用例，运行时会提示未发现测试。

```bash
# 仅发现已有后端测试，不启动服务或执行文件操作。
dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --list-tests

pnpm --dir OpenERP.Web test
pnpm --dir OpenERP.Web exec playwright install chromium
pnpm --dir OpenERP.Web test:e2e
```

已有后端测试仍通过 `OPENERP_TEST_API_URL` 指向预先启动的 API，其中一个用例会重启当前 checkout 的 SeaweedFS；执行前应确认目标是可用于文件测试的环境。Aspire 测试环境的创建尚未接入现有用例。浏览器测试的目标地址通过 `PLAYWRIGHT_BASE_URL` 指定，使用 Aspire 实际分配的前端地址。
