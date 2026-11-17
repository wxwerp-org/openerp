# 测试说明

本目录集中放置 .NET 测试项目。仓库根目录的 TypeScript Playwright 测试位于 [`e2e/`](../e2e/)；其入口配置和命令位于根目录的 [`playwright.config.ts`](../playwright.config.ts) 与 [`package.json`](../package.json)。本地开发、构建和 CI 说明另见[开发说明](../docs/development.md#构建与测试)。

## 目录与运行范围

| 路径 | 用途 | 默认 `OpenERP.slnx` |
| --- | --- | --- |
| [`OpenERP.Api.Tests`](OpenERP.Api.Tests/) | 后端单元、集成和基础设施测试；以 `Unit/`、`Integration/`、`Infrastructure/` 分组 | 包含，运行全部用例，不按目录过滤 |
| [`OpenERP.Api.LiveIntegrationTests`](OpenERP.Api.LiveIntegrationTests/) | 访问真实 wxw 的受控分页一致性验收 | 不包含，必须显式指定项目 |
| [`../e2e`](../e2e/) | Playwright 浏览器测试；包括展示用例和订单同步端到端验收 | 由根目录 Playwright 命令运行 |

## 后端常规测试

`OpenERP.Api.Tests` 使用 xUnit v3 和 .NET 10 原生 Microsoft.Testing.Platform。普通 CI/本地回归直接运行 solution，其中所有后端测试均执行，包括需要容器的 Aspire 集成测试：

```bash
dotnet test --solution OpenERP.slnx --zero-tests-policy strict --fail-skips on
```

该入口不需要真实 wxw Token，也不读取开发用的 `OPENERP_TEST_POSTGRES`。集成测试通过 `Aspire.Hosting.Testing` 的 `DistributedApplicationTestingBuilder` 按真实 AppHost 图启动隔离资源；测试使用临时密钥与容器生命周期，等待 API/依赖健康后通过 Aspire 提供的动态端点访问服务，并在结束时清理。运行需要 Docker 兼容的容器运行时和受信任的 HTTPS 开发证书。

这个夹具遵循 [Aspire 官方测试说明](https://aspire.dev/testing/overview/)：Aspire 测试用于验证 AppHost 管理的服务和依赖协作，端点与生命周期由测试宿主管理。按业务逻辑、HTTP/API 与 PostgreSQL 交互、基础设施就绪行为选择对应目录；具体用例以 [`OpenERP.Api.Tests`](OpenERP.Api.Tests/) 为准。

## 真实 wxw 分页验收

`OpenERP.Api.LiveIntegrationTests` 保留 SID `644928` 的只读分页一致性验收：同一读取器分别按每页 20 和 100 条读取全量订单，并核对源端总数、唯一 ID、订单 ID 顺序、子单归属和跳过原因。该店需有超过 100 单。项目不属于默认 solution，也不依赖或访问本地 PostgreSQL。

测试要求进程环境中的 `OPENERP_TEST_ERP_TOKEN`，且 Token 必须有权读取该店。凭证缺失时测试以失败结束，不跳过。该项目关闭 NuGet lock file，显式还原时使用普通 NuGet 解析，不加 `--locked-mode`：

```bash
dotnet restore tests/OpenERP.Api.LiveIntegrationTests/OpenERP.Api.LiveIntegrationTests.csproj
dotnet test --project tests/OpenERP.Api.LiveIntegrationTests/OpenERP.Api.LiveIntegrationTests.csproj --no-restore --zero-tests-policy strict --fail-skips on
```

不要将开发数据库配置为测试数据源；真实订单同步和入库核对应通过下文的隔离 Playwright E2E 流程完成。

## Playwright 端到端测试

Playwright 的配置、依赖和命令都在仓库根目录。`globalSetup` 调用官方 `aspire start --isolated`、`aspire wait web`、`aspire describe --format Json` 和 `aspire stop`。现有 AppHost 在显式 `E2E:Mode` 配置下使用 session 容器和临时文件系统，不挂开发数据卷；连接串、动态 HTTPS 端点与 Node CA 配置直接来自 Aspire。默认 `stub` 模式启动本地 wxw HTTP 替身，只替换上游地址；`OpenERP.Api` 中的真实 `WxwErpClient`、订单同步、事务和 PostgreSQL 写入仍会执行。`Erp:BaseUrl` 覆盖只在 Development 生效，默认与其他环境仍使用固定 wxw 地址。

浏览器用例均通过真实认证访问隔离 API。缺少认证配置、wxw Token 或端点时会明确失败；不以跳过或预览模式代替测试。部分工作台展示用例继续用 `page.route` 提供页面数据。订单同步用例位于 [`e2e/order-sync.spec.ts`](../e2e/order-sync.spec.ts)，不拦截或模拟 OpenERP API，而是通过 UI 对接真实 API 和同步流程：stub 提供跨页的 21 条订单，浏览器执行两次手动同步，并将订单、平台子单和商品行与预期数据逐字段、按重复行数量进行精确对账。

安装与项目版本一致的 Aspire CLI，在仓库根目录准备应用、前端依赖、Chromium 和 HTTPS 信任后运行：

```bash
dotnet build OpenERP.slnx
dotnet build e2e/verify-orders.cs
pnpm install --frozen-lockfile
pnpm --dir OpenERP.Web install --frozen-lockfile
pnpm exec playwright install chromium
aspire certs trust
pnpm test:e2e
```

同一 checkout 若已有 AppHost 在运行，入口会拒绝执行；先结束该会话，或在另一个 worktree 运行测试。改动 C# 代码后重新执行上述构建。数据库断言由 [`e2e/verify-orders.cs`](../e2e/verify-orders.cs) 直接查询本次 Aspire 数据库，不启动辅助 HTTP 服务。测试需要容器运行时、Node.js/pnpm、Chromium 和受信任的开发证书。Playwright 使用单 worker、`forbidOnly` 和原生 HTML/JSON 报告；原生退出码决定 Actions 结果，不再维护自定义 reporter。核对报告中的通过、失败和跳过明细，不将跳过报告为通过。

### 真实 wxw E2E

真实 wxw 模式由 [`playwright.yml`](../.github/workflows/playwright.yml) 的 `workflow_dispatch` 显式触发，选择 `wxw=live`；`shop_sid` 默认是 `30`，凭证来自 `wxw-e2e` environment 的 `WXW_ERP_TOKEN`，以 `OPENERP_TEST_ERP_TOKEN` 传给测试。Live 模式没有 Token 会在 setup 阶段失败，不会自动改跑 stub 或跳过。

工作流先运行前述 SID `644928` 的 C# 分页一致性验收，再运行真实认证浏览器流程。Playwright 使用独立 Aspire 测试数据库和动态端点，针对 `shop_sid` 指定的店铺（默认 SID `30`）经 UI 触发两次手动同步，随后以真实 wxw 响应与 PostgreSQL 中的订单、平台子单和商品行进行精确对账。上游读取时按现有业务规则过滤的订单可能计入同步任务的业务跳过数；这不等于测试运行器跳过了用例。它不使用开发数据库或 `OPENERP_TEST_POSTGRES`。真实 Token 和真实源数据属于受控环境；本地 stub/常规 CI 结果不等于真实 wxw 验收结果。

## CI

- [`ci-backend.yml`](../.github/workflows/ci-backend.yml)：锁定还原并构建默认 solution，完整运行 C# 常规测试；另外还原、构建 Live 项目但不访问真实 wxw。通过运行器的 strict zero-tests/fail-skips 策略拒绝零用例和跳过，并保存原生 xUnit CTRF 报告。
- [`ci-web.yml`](../.github/workflows/ci-web.yml)：执行前端 lint、typecheck、Vitest 单元测试和生产构建。
- [`playwright.yml`](../.github/workflows/playwright.yml)：单独运行 Playwright；PR 使用 stub 模式，手动派发可选真实 wxw。原生 HTML/JSON 测试报告作为 Actions artifact 保存，工作流保留测试运行器的失败结果。
