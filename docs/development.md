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

[router/index.ts](../OpenERP.Web/src/router/index.ts) 定义工作台、授权店铺、产品、订单、业务配置、用户与新品开发路由，[views](../OpenERP.Web/src/views/) 存放页面，[components](../OpenERP.Web/src/components/) 提供可复用工作台卡片、导航和控件。[styles](../OpenERP.Web/src/styles/) 按区域组织共享样式，[elementPlusStyles.ts](../OpenERP.Web/src/elementPlusStyles.ts) 集中声明按需加载的 Element Plus 样式。

## 构建与测试

工具版本以 [global.json](../global.json)、[.node-version](../.node-version)、[本地工具清单](../.config/dotnet-tools.json) 和 [package.json](../OpenERP.Web/package.json) 为准。

安装依赖后，在仓库根目录构建并运行测试：

```bash
dotnet build OpenERP.slnx --no-restore
dotnet test --solution OpenERP.slnx
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web typecheck
pnpm --dir OpenERP.Web build
pnpm --dir OpenERP.Web test
```

### 前端静态检查

[ESLint 配置](../OpenERP.Web/eslint.config.js) 使用 flat config，以 `@eslint/js`、`typescript-eslint` 和 `eslint-plugin-vue` 的官方配置为基础。Vue 采用官方 error-prevention（`flat/essential`）规则集，并补充官方 `flat/recommended` 中 10 条非排版正确性/安全规则（如 `vue/no-v-html`、`vue/require-explicit-emits`），覆盖模板和 `<script setup lang="ts">`；单元测试与浏览器测试分别使用 `@vitest/eslint-plugin` 和 `eslint-plugin-playwright` 的推荐规则。这套组合与仓库现有 Node.js 26、TypeScript 6、Vue 3.5 的版本范围兼容，且不引入旧式 `.eslintrc`。

检查范围为 `src/**/*.{ts,vue}`、`tests/**/*.ts`、前端根目录的构建配置和 ESLint 配置本身；`node_modules`、`dist`、`coverage`、`playwright-report` 与 `test-results` 不参与检查。`src` 使用浏览器全局变量，构建配置使用 Node.js 全局变量，单元测试同时启用浏览器、Node.js 与 Vitest 环境，端到端测试使用 Playwright 规则并同时启用浏览器与 Node.js 全局变量（浏览器全局变量用于 `page.evaluate` 等页面内回调）。

```bash
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web lint:fix
```

`lint` 只读检查，不修改文件；`lint:fix` 自动修复支持的问题，仍会报告无法自动修复的问题。两者都使用 `--max-warnings 0`，发现问题时返回非零退出码，后续 CI 可直接调用 `lint`。

ESLint 在这里只负责代码质量和潜在错误，不启用缩进、引号、分号等格式化规则；两格缩进由 [`.editorconfig`](../.editorconfig) 约定，单引号、不加分号等继续沿用现有代码约定，避免与格式化工具产生重复或冲突规则。类型检查仍由独立的 `pnpm --dir OpenERP.Web typecheck`（`vue-tsc --noEmit`）完成，lint 不使用类型信息。规则应优先修复实际问题；确需例外时只在对应行使用带原因的 `eslint-disable-next-line <rule>`，不要扩大全局忽略范围或关闭整类规则。

### 测试环境

后端使用 xUnit v3、原生 .NET 10 Microsoft.Testing.Platform 和 Aspire.Hosting.Testing，配置见 [global.json](../global.json) 与[测试项目](../OpenERP.Api.Tests/OpenERP.Api.Tests.csproj)。订单 PostgreSQL 与文件存储测试共用隔离 Aspire 测试宿主，需要容器环境和受信任的开发证书。宿主自行启动和清理资源，订单用例另建并清理独立数据库，无需 `OPENERP_TEST_POSTGRES`。回归用例见 [OpenERP.Api.Tests](../OpenERP.Api.Tests/)。

前端单元测试位于 [tests/unit](../OpenERP.Web/tests/unit/)，使用 Vitest、Vue Test Utils 和 jsdom。依赖和执行脚本以 [package.json](../OpenERP.Web/package.json) 为准。

[WebInstallerTests](../OpenERP.Api.Tests/WebInstallerTests.cs) 使用实际 AppHost 安装参数，在临时目录执行 pnpm 验证无 TTY 的依赖重建与冻结锁文件行为。需要 PATH 中有 Node.js 和 pnpm；fixture 只使用本地依赖，不连接 ERP，也不修改开发目录的 `node_modules`。

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
| `GET /api/erp/order-sync-settings` | 仅 Development 提供；返回定时同步的 `enabled`、`shopSids` 和只读 `intervalMinutes` |
| `PUT /api/erp/order-sync-settings` | 仅 Development 提供；保存启用状态和店铺选择；启用时至少选择一家店铺，保存后动态通知调度器 |
| `GET /api/erp/order-sync-settings/shops` | 仅 Development 提供；分页拉取 Token 可见的全部店铺基础信息，不查询平台授权状态 |

店铺列表请求失败时返回错误；单店平台授权状态查询失败时保留店铺，将授权状态标为未知。同步任务受理后还会检查店铺是否对当前 Token 可见，`202` 不代表订单已成功导入。实现见 [ErpEndpoints](../OpenERP.Api/Erp/ErpEndpoints.cs) 和 [OrderSyncEndpoints](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

## 订单同步边界

- API 在 Development 启动时应用 EF Core 迁移。主单以 ERP 内部 ID 标识，平台 PO 和商品行关联主单；订单时间保留 ERP 显示原值及来源。
- 完整读取并校验列表和详情后才写库；读取失败时不保存本批结果，数据库保存失败时整批回滚。
- 归属异常的订单整单跳过，任务显示部分成功和跳过原因。只刷新本批通过校验的订单，未返回或被跳过的已有订单会保留；已接受订单的子项按本次结果更新。
- 同步任务状态保存在 API 内存中，每店保留最近一次任务，整个 API 同时只运行一个任务。刷新网页可恢复状态，API 重启后状态丢失；手动同步需重新触发，定时同步会在下一周期重跑，已保存的订单仍在数据库中。
- 列表按 ERP ID 排序，并核对前后计数、每页条数及 ID 唯一性。上游使用偏移分页，没有一致性快照：扫描期间若不同订单同时进入和退出可见范围，即使总数不变仍可能漏读。两种页长得到相同结果仅能证明当次扫描一致。

相关实现：[订单读取器](../OpenERP.Api/Erp/WxwErpClient.Orders.cs)、[任务状态](../OpenERP.Api/Orders/OrderSyncJobs.cs)、[订单保存](../OpenERP.Api/Orders/OrderStore.cs)。

## 定时同步边界

- 定时同步与手动同步同样只在 Development 提供。店铺页的「定时同步」卡片只配置是否启用和选择店铺，不提供周期配置。
- 周期来自 API 配置 `OrderSync:SchedulerIntervalMinutes`（默认 `30`，允许 `1`–`1440` 分钟），启动时校验；修改后需重启 API，保存设置本身不需要重启。每轮受理后等待一个周期再尝试下一轮；若上一轮仍在运行，本轮跳过并等下一周期。启用后首次执行同样等待一个周期。
- 设置保存在 PostgreSQL 的 `OrderSyncSettings` 单行表（`Id` 固定为 `1`，数据库检查约束拒绝第二行），迁移为 `AddOrderSyncSettings`。保存后 API 唤醒调度器重新读取设置；启用后首次执行仍等待一个完整周期。
- 启用时必须至少选择一家店铺，空选返回 `400`，前端也会禁用保存。历史空选数据不会触发同步。启用还要求已配置私有 ERP AccessToken；未配置时保存启用状态返回 `503`，前端禁用开关和保存。
- 选店列表来自全部 Token 可见店铺（分页拉取，只取基础信息），不受店铺表格分页限制；提供「全选可见店铺」和清空辅助。保存设置时不再调用 ERP 校验店铺可见性：Token 权限变化后该店会在定时轮次中记录失败，其他店铺继续，可在卡片中清空或重新选择。
- 定时批次按顺序逐店同步，单店失败只记录该店失败并继续后续店铺，不在同一周期内自动退避重试；下一周期会重新扫描全部选中店铺，批次结束后也可手动补跑。重复同步复用订单记录的幂等 upsert，不会写入重复订单。
- 手动与定时共用全局单任务互斥：定时批次运行期间手动同步返回 `409`；手动同步运行期间定时周期直接跳过并等待下一周期。关闭定时同步只阻止后续周期，已受理的批次会继续执行完当前店铺列表。
- 不做跨进程或重启的作业持久化、断点恢复：任务状态仍在 API 内存中，重启后丢失；定时同步会在下一个周期重新执行选中店铺，已保存订单不受影响。

相关实现：[定时同步设置](../OpenERP.Api/Orders/OrderSyncSettings.cs)、[调度器](../OpenERP.Api/Orders/OrderSyncScheduler.cs)、[接口](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

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

- Aspire 的 `web-installer` 没有交互终端。安装参数使用 `--config.confirmModulesPurge=false`，允许 pnpm 11 自动重建因 store 路径等变化而不兼容的 `node_modules`，避免 `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY`；仍保留 `--frozen-lockfile`，不改变手工安装的确认行为。
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

如果当前 checkout 已有 PostgreSQL `18` 创建的开发卷，首次切换到 Alpine 时请使用全新的默认 PostgreSQL 卷。例如，从新的 checkout 路径启动，Aspire 会为该路径创建默认卷。

## 使用 AI 开发工具

仓库代理规则由 [AGENTS.md](../AGENTS.md) 提供，专项流程位于 `.agents/skills/`。

仓库的 [.mcp.json](../.mcp.json) 与 [.vscode/mcp.json](../.vscode/mcp.json) 使用 `aspire agent init` 生成的 `aspire agent mcp` 配置。使用支持 MCP 的客户端时，确保其 PATH 能找到 `aspire`。终端命令通过 `dotnet tool run aspire` 使用仓库锁定的本地 CLI，两者是不同入口。
