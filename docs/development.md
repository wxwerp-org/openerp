# 开发说明

本页提供代码导航、构建测试、接口行为和本地排障说明。环境准备、依赖安装及启动见 [README](../README.md#快速开始)；贡献要求与协作规则见[贡献指南](../CONTRIBUTING.md)。

## 代码导航

| 目录 | 内容 |
| --- | --- |
| [OpenERP.Web](../OpenERP.Web/) | Vue 前端与单元测试 |
| [OpenERP.Api](../OpenERP.Api/) | API、ERP 对接、订单同步与数据存储 |
| [OpenERP.AppHost](../OpenERP.AppHost/) | 本地服务编排 |
| [OpenERP.ServiceDefaults](../OpenERP.ServiceDefaults/) | 健康检查、遥测与服务发现 |
| [OpenERP.Api.Tests](../OpenERP.Api.Tests/) | 后端单元测试和集成测试 |

[router/index.ts](../OpenERP.Web/src/router/index.ts) 定义 `/` 首页和 `/shops` 店铺页路由，[views](../OpenERP.Web/src/views/) 存放页面，[styles.css](../OpenERP.Web/src/styles.css) 提供共享样式。

## 构建与测试

工具版本以 [global.json](../global.json)、[.node-version](../.node-version)、[本地工具清单](../.config/dotnet-tools.json) 和 [package.json](../OpenERP.Web/package.json) 为准。

安装依赖后，在仓库根目录构建并运行测试：

```bash
dotnet build OpenERP.slnx --no-restore
dotnet test --solution OpenERP.slnx
pnpm --dir OpenERP.Web typecheck
pnpm --dir OpenERP.Web build
pnpm --dir OpenERP.Web test
```

### 测试环境

后端使用 xUnit v3、原生 .NET 10 Microsoft.Testing.Platform 和 Aspire.Hosting.Testing，配置见 [global.json](../global.json) 与[测试项目](../OpenERP.Api.Tests/OpenERP.Api.Tests.csproj)。订单 PostgreSQL 与文件存储测试共用隔离 Aspire 测试宿主，需要容器环境和受信任的开发证书。宿主自行启动和清理资源，订单用例另建并清理独立数据库，无需 `OPENERP_TEST_POSTGRES`。回归用例见 [OpenERP.Api.Tests](../OpenERP.Api.Tests/)。

前端单元测试位于 [tests/unit](../OpenERP.Web/tests/unit/)，使用 Vitest、Vue Test Utils 和 jsdom。依赖和执行脚本以 [package.json](../OpenERP.Web/package.json) 为准。

浏览器测试入口由 [Playwright 配置](../OpenERP.Web/playwright.config.ts) 指定。执行前确认其 `testDir` 下有实际用例，启动应用并等待 `web` 就绪，将 `PLAYWRIGHT_BASE_URL` 设置为实际地址，再安装浏览器并执行测试：

```bash
pnpm --dir OpenERP.Web exec playwright install chromium
pnpm --dir OpenERP.Web test:e2e
```

### 真实 ERP 验证

常规后端测试自行准备隔离 PostgreSQL，不需要连接开发数据库。可选的 [OrderLiveAcceptanceTests](../OpenERP.Api.Tests/OrderLiveAcceptanceTests.cs) 使用进程环境变量：

| 变量 | 用途与前提 |
| --- | --- |
| `OPENERP_TEST_ERP_TOKEN` | 有权读取目标店铺的 Token；启用 SID `644928` 的只读分页验证，该店需有超过 100 单 |
| `OPENERP_TEST_POSTGRES` | 与上述 Token 一起启用 SID `30` 的源端与入库结果比对；指向已完成该店手动同步的数据库 |

数据库比对用例的前置步骤是在浏览器完成 SID `30` 的两次手动同步；测试本身只读源端和数据库，不执行同步或建表。未设置相应变量时用例跳过。凭证仅通过进程环境提供，不写入文档或版本库；测试命令见本页[构建与测试](#构建与测试)。

## ERP 接入与接口

上游地址固定为 `https://v3-api.wxwerp.com`。AppHost 将 User Secrets 中的 `Parameters:wxw-erp-access-token` 注入 API 的 `Erp__AccessToken`；直接配置 API 时使用后者。Token 已配置只表示配置值存在，不代表凭证有效、接口权限充分或店铺平台授权有效。前端只读取配置状态，不获取 Token。

| 接口 | 行为 |
| --- | --- |
| `GET /api/erp/config` | 返回配置状态、上游地址和 `orderSyncEnabled`；仅 Development 开启同步能力 |
| `GET /api/erp/shops?page=1&pageSize=20` | 查询当前 Token 可见的 TemuPop 店铺；每页 1–100 条，数量与页数来自 ERP |
| `POST /api/erp/shops/{sid}/order-sync` | 仅 Development 提供；受理返回 `202`，已有同步返回 `409`，未配置 Token 返回 `503` |
| `GET /api/erp/shops/{sid}/order-sync` | 仅 Development 提供；查询该店最近任务，无任务返回 `204` |

店铺列表请求失败时返回错误；单店平台授权状态查询失败时保留店铺，将授权状态标为未知。同步任务受理后还会检查店铺是否对当前 Token 可见，`202` 不代表订单已成功导入。实现见 [ErpEndpoints](../OpenERP.Api/Erp/ErpEndpoints.cs) 和 [OrderSyncEndpoints](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

## 订单同步边界

- API 在 Development 启动时应用 EF Core 迁移。主单以 ERP 内部 ID 标识，平台 PO 和商品行关联主单；订单时间保留 ERP 显示原值及来源。
- 完整读取并校验列表和详情后才写库；读取失败时不保存本批结果，数据库保存失败时整批回滚。
- 归属异常的订单整单跳过，任务显示部分成功和跳过原因。只刷新本批通过校验的订单，未返回或被跳过的已有订单会保留；已接受订单的子项按本次结果更新。
- 同步任务状态保存在 API 内存中，每店保留最近一次任务，整个 API 同时只运行一个任务。刷新网页可恢复状态，API 重启后状态丢失，需手动重跑；已保存的订单仍在数据库中。
- 列表按 ERP ID 排序，并核对前后计数、每页条数及 ID 唯一性。上游使用偏移分页，没有一致性快照：扫描期间若不同订单同时进入和退出可见范围，即使总数不变仍可能漏读。两种页长得到相同结果仅能证明当次扫描一致。

相关实现：[订单读取器](../OpenERP.Api/Erp/WxwErpClient.Orders.cs)、[任务状态](../OpenERP.Api/Orders/OrderSyncJobs.cs)、[订单保存](../OpenERP.Api/Orders/OrderStore.cs)。

## 本地诊断

从仓库根目录检查实际资源名、端点和就绪状态，再查看 API 日志与追踪：

```bash
dotnet tool run aspire describe --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire wait web --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire otel logs api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire otel traces api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

浏览器日志使用 `web-browser-logs` 子资源，需要 Chrome 或 Edge。打开受跟踪的浏览器后查看该资源的控制台日志：

```bash
dotnet tool run aspire resource web-browser-logs open-tracked-browser --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire logs web-browser-logs --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

资源名以 `describe` 输出为准。浏览器输出不在 `aspire otel logs web` 中。

### 启动和健康检查

- [AppHost 启动配置](../OpenERP.AppHost/Properties/launchSettings.json) 让 `localhost`、`127.0.0.1`、`::1` 绕过 HTTP 代理，避免本地 gRPC 被系统代理转发，不修改全局代理设置。
- [Vite 配置](../OpenERP.Web/vite.config.ts) 将浏览器的 `/api` 请求转发到 Aspire 注入的 API 地址；仅开发代理接受后端自签证书。
- SeaweedFS 的 Master/Volume 健康检查之外，AppHost 另用签名 S3 `ListBuckets` 检查等待存储就绪，再启动 API 建桶。这是项目自定义的启动保护，不保证建桶必然成功；存储后续失去健康也不会自动暂停已运行的 API。
- Development 环境的 `/health` 检查 PostgreSQL、Redis 和 SeaweedFS S3 连接；`/alive` 只检查进程存活。

### 开发数据与密钥

PostgreSQL、Redis 和 SeaweedFS 均使用持久容器与数据卷。PostgreSQL 挂载到 `/var/lib/postgresql`，Redis 和 SeaweedFS 挂载到 `/data`。卷名由 Aspire 根据应用身份与资源名生成，以当前环境为准；macOS Docker Desktop 的卷保存在其 Linux 虚拟机内，不在源码目录。

开发密码保存在 .NET User Secrets 中。只查看密钥文件位置、避免打印秘密值时使用：

```bash
dotnet tool run aspire secret path --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

正常停止会保留持久资源；`aspire stop --force` 会删除持久资源，不能用于普通停止。保留数据卷时也应保留开发密钥及 `UserSecretsId`，避免密码不匹配。Redis 使用快照持久化，异常断电可能丢失最近的内存写入。

PostgreSQL `18-alpine` 和 Redis `8-alpine` 使用浮动大版本标签，小版本可能随镜像更新变化，本机缓存也会影响实际运行版本。SeaweedFS `4.53` 的普通发布镜像已基于 Alpine。[AppHost 镜像配置](../OpenERP.AppHost/AppHost.cs) · [SeaweedFS 发布构建](https://github.com/seaweedfs/seaweedfs/blob/4.53/.github/workflows/container_release_unified.yml) · [SeaweedFS Dockerfile](https://github.com/seaweedfs/seaweedfs/blob/4.53/docker/Dockerfile.go_build)

#### 从 PostgreSQL `18` 迁移已有开发数据

Alpine 的 libc 会改变 PostgreSQL 默认文本排序。旧卷记录了 Debian 的 collation 版本，直接挂载到 Alpine 时，从 `template1` 创建数据库会失败。因此新 AppHost 使用以 `-postgres-alpine-data` 结尾的独立卷，原 `-postgres-data` 卷保留。新卷首次启动时为空，有旧数据的开发环境需要逻辑导出与恢复。[PostgreSQL 排序规则](https://www.postgresql.org/docs/18/collation.html)

1. 在旧版本中通过 Aspire 停止 `api`，确认它已停止并暂停其他数据库写入，保持 PostgreSQL 运行。使用 `dotnet tool run aspire describe postgres --format Json --apphost OpenERP.AppHost/OpenERP.AppHost.csproj` 查看容器资源的 `name` 和 `volumes`。确认源卷以 `-postgres-data` 结尾，将 `OLD_POSTGRES` 设为容器名，在新的私有备份目录导出应用库。导出成功后再切换版本。

   ```bash
   umask 077
   docker exec "$OLD_POSTGRES" sh -ec 'export PGPASSWORD="$POSTGRES_PASSWORD"; exec pg_dump -Fc -U "$POSTGRES_USER" -d openerp' > openerp.dump
   ```

2. 正常停止旧 AppHost 后，在原 checkout 路径启动新版本。迁移完成前暂停同步及其他数据库写入。等待 PostgreSQL 与 API 就绪后，通过 Aspire 停止 `api` 并确认它已停止。再次查询 PostgreSQL 资源，确认容器挂载了新的 `-postgres-alpine-data` 卷，再将 `NEW_POSTGRES` 设为该容器名。

3. 确认目标库只包含首次启动生成的结构和迁移记录，并检查三张订单表的行数均为 0。目标库已有数据时，先备份这些数据并确定合并方式。确认目标可恢复后，`pg_restore` 会在一个事务内替换备份中的数据库对象和数据。

   ```bash
   docker exec -i "$NEW_POSTGRES" sh -ec 'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d openerp' <<'SQL'
   SELECT (SELECT count(*) FROM "Orders") AS orders,
          (SELECT count(*) FROM "OrderLines") AS order_lines,
          (SELECT count(*) FROM "PlatformOrders") AS platform_orders;
   SQL
   ```

   ```bash
   docker exec -i "$NEW_POSTGRES" sh -ec 'export PGPASSWORD="$POSTGRES_PASSWORD"; exec pg_restore --clean --if-exists --single-transaction --exit-on-error -U "$POSTGRES_USER" -d openerp' < openerp.dump
   ```

4. 恢复 API 后检查 `/health`，对照旧库核对订单、订单行和平台订单。保留原卷、开发密钥和备份。需要回退时，停止新 AppHost，以原 checkout 路径运行旧版本，重新使用原卷。原卷包含切换前的数据，切换后产生的写入需另行保留。

## 使用 AI 开发工具

仓库代理规则由 [AGENTS.md](../AGENTS.md) 提供，专项流程位于 `.agents/skills/`。

仓库的 [.mcp.json](../.mcp.json) 与 [.vscode/mcp.json](../.vscode/mcp.json) 使用 `aspire agent init` 生成的 `aspire agent mcp` 配置。使用支持 MCP 的客户端时，确保其 PATH 能找到 `aspire`。终端命令通过 `dotnet tool run aspire` 使用仓库锁定的本地 CLI，两者是不同入口。
