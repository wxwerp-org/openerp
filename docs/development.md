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

[router/index.ts](../OpenERP.Web/src/router/index.ts) 定义工作台、授权店铺、产品、订单、业务配置、用户与新品开发路由，[views](../OpenERP.Web/src/views/) 存放页面，[components](../OpenERP.Web/src/components/) 提供可复用工作台卡片、导航和控件。[styles](../OpenERP.Web/src/styles/) 按区域组织共享样式，列表、控件与页面类按用途拆分。

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

浏览器测试入口由 [Playwright 配置](../OpenERP.Web/playwright.config.ts) 指定。默认由配置中的 `webServer` 自动执行生产构建并在 `127.0.0.1:4173` 启动预览服务，无需手工启动应用；安装浏览器后直接运行：

```bash
pnpm --dir OpenERP.Web exec playwright install chromium
pnpm --dir OpenERP.Web test:e2e
```

未设置 `OPENERP_TEST_ADMIN_PASSWORD` 时，工作台用例通过页面级 mock 会话执行，账户与会话用例（[authentication.spec.ts](../OpenERP.Web/tests/e2e/authentication.spec.ts)）跳过；手动派发[前端工作流](../.github/workflows/ci-web.yml)时运行这类无需后端的浏览器用例，PR 不自动运行浏览器测试。设置 `PLAYWRIGHT_BASE_URL` 指向真实宿主时复用外部服务，不再启动 `webServer`。

账户端到端用例需要真实后端与 `OPENERP_TEST_ADMIN_PASSWORD`，会修改测试账户资料和密码。请使用隔离测试宿主，避免对日常开发账户运行这些用例：

```bash
OPENERP_RUN_BROWSER_TESTS=1 dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --filter-class '*BrowserAcceptanceTests'
```

该入口创建测试专用数据库，从未初始化状态启动 HTTPS 前端，将实际端点及本次测试要设置的密码通过环境传给 Playwright，并串行运行浏览器用例；测试密码不会注入 API 作为预置密码。初始化用例保存设置页和设置成功后的登录页截图，完成后由 Aspire 测试夹具清理测试资源。它先通过 .NET 严格校验前端证书，再将该证书的公钥临时提供给 Node 客户端，不跳过证书校验；临时文件在结束后删除。独立运行 Playwright 时，Node 如不识别本机开发证书，需用 `NODE_EXTRA_CA_CERTS` 指定对应的公开 PEM 证书。不设置开关时，此浏览器入口明确跳过，不计作浏览器验收通过。

### 真实 ERP 验证

常规后端测试自行准备隔离 PostgreSQL，不需要连接开发数据库。可选的 [OrderLiveAcceptanceTests](../OpenERP.Api.Tests/OrderLiveAcceptanceTests.cs) 使用进程环境变量：

| 变量 | 用途与前提 |
| --- | --- |
| `OPENERP_TEST_ERP_TOKEN` | 有权读取目标店铺的 Token；启用 SID `644928` 的只读分页验证，该店需有超过 100 单 |
| `OPENERP_TEST_POSTGRES` | 与上述 Token 一起启用 SID `30` 的源端与入库结果比对；指向已完成该店手动同步的数据库 |

数据库比对用例的前置步骤是在浏览器完成 SID `30` 的两次手动同步；测试本身只读源端和数据库，不执行同步或建表。未设置相应变量时用例跳过。凭证仅通过进程环境提供，不写入文档或版本库；测试命令见本页[构建与测试](#构建与测试)。

### GitHub Actions

[后端 PR 检查](../.github/workflows/ci-backend.yml)按照 [Aspire 官方 CI 指导](https://aspire.dev/testing/testing-in-ci/)在 Ubuntu 上还原、构建并无过滤执行整个 solution。容器由既有 Aspire 测试夹具启动和清理，YAML 不自行编排容器；Node.js/pnpm 用于既有 `WebInstallerTests`，HTTPS 信任用于既有集成测试。构建、容器启动、测试断言或清理失败会使 job 失败。

测试使用运行器原生 `--zero-tests-policy strict` 拒绝零用例，原生 xUnit CTRF 报告保存为 `backend-results` artifact。CI 不注入真实 ERP 凭证，也不设置浏览器启用开关；两个 `OrderLiveAcceptanceTests` 和 `BrowserAcceptanceTests` 保留上述条件跳过，报告中的跳过不计作验证通过。完整后端套件包含真实容器集成测试，执行耗时不能按纯单元测试估算。

[前端 PR 检查](../.github/workflows/ci-web.yml)继续自动执行 lint、类型检查、单元测试与构建；其浏览器 job 只在 `workflow_dispatch` 时执行，手动运行失败仍会传递到结果汇总。

## 管理员账户与会话

系统只有一个管理员，用户名固定为 `admin`，不提供注册、员工权限或邮箱找回密码。姓名必填（最多 20 个字符），手机号可选（最多 20 个字符），邮箱可选并校验格式；手机号、邮箱仅作为个人资料。菜单中的资料与最近登录时间来自数据库，保存资料不会改变最近登录时间。

### 初始化与密码重置

新部署启动后只有未初始化的管理员占位账户，不生成或保存预置密码。部署完成后立即打开应用 HTTPS 地址，在「设置管理员密码」页输入并确认密码，然后使用固定用户名 `admin` 登录。密码长度为 6–20 个字符，不自动去除首尾空白。AppHost 与直接运行的 API 均不需要初始密码配置。

初始化状态以数据库中 `PasswordHash` 是否为 `NULL` 区分。启动只在管理员行缺失时创建占位行，重复启动不覆盖已有密码或资料。初始化通过数据库条件更新完成，并发只能一方成功；后续提交不会覆盖密码。初始化不签发会话，也不更新最近登录时间。

升级迁移保留全部存量密码、资料及会话，包括旧的首次改密账户。首次改密限制取消，原受限会话也能正常访问业务，仍按原时间到期。初始化成功记录 UTC 时间、连接来源 IP 和 User-Agent，不记录密码，不新增审计表。反向代理环境下连接来源可能是代理地址；此日志不直接信任任意客户端转发头。

初始化入口匿名开放，CSRF 校验不是部署者身份凭证。公网可达时第一个完成设置的人将成为管理员，因此部署输出应直接给出应用地址，并提示部署完成后立即初始化。全新实例被抢占且尚无业务数据时可重新部署；已有数据时通过命令行重置恢复，不删除数据库或数据卷。

Development 环境先应用 EF Core 迁移，再准备管理员占位账户；其他环境仍须由运维预先应用迁移。本次迁移删除旧的首次改密字段，升级时应停止旧 API、应用迁移，再启动新版本，不能让旧 API 继续访问新结构。若数据库仍有未初始化的 `NULL` 密码占位行，向下迁移恢复 `NOT NULL` 会被拒绝，不会自动填入替代密码。

忘记密码时，在已配置目标数据库连接的 API 运行环境执行 `reset-admin-password` 命令。该命令只连接数据库更新已有管理员，不启动 HTTP 服务、订单任务或存储服务，不迁移数据库，不创建缺失账户。已有未初始化占位账户时，命令设置最终密码并完成初始化，此后网页初始化会被拒绝。交互模式隐藏输入并要求确认；自动化通过显式 `--stdin` 从标准输入提供密码，不接受明文密码命令行参数。命令失败返回非零退出码；不要清空数据库或删除数据卷来重置密码。

例如，构建后从仓库根目录执行以下命令；发布环境将 DLL 路径替换为实际发布路径。进程配置须包含目标数据库的 `ConnectionStrings__openerp`，它不会自动继承另一个 Aspire 进程中的环境变量。

```bash
dotnet OpenERP.Api/bin/Debug/net10.0/OpenERP.Api.dll reset-admin-password
```

自动化时由 Secret 管理工具将一行新密码传入相同命令的标准输入，并追加 `--stdin`；不要将真实密码写进脚本或终端命令历史。

### 会话规则与接口

每次登录建立独立的 24 小时会话，不滑动续期，浏览器关闭或 API 重启不会提前撤销有效会话。普通退出只撤销当前会话；网页修改密码和命令行重置会撤销所有会话，所有设备需重新登录。网页修改密码必须提供旧密码；命令行重置直接设置最终密码。已受理的订单同步继续执行。

Cookie 使用 `Secure`、`HttpOnly`、`SameSite=Lax`，必须通过 HTTPS 访问。会话和 Data Protection 密钥存储于 PostgreSQL；数据库不可用时返回服务错误，不将故障视为退出登录。

| 接口 | 用途 |
| --- | --- |
| `GET /api/auth/csrf` | 匿名获取 `{ requestToken }` |
| `GET /api/auth/status` | 匿名查询 `{ initialized }`，响应禁止缓存 |
| `POST /api/auth/initialize` | 未初始化时提交 `{ newPassword }` 设置管理员密码，成功后正常登录 |
| `POST /api/auth/login` | 提交 `{ username, password }` 登录 |
| `GET /api/auth/me` | 查询当前账户及会话到期时间 |
| `POST /api/auth/logout` | 撤销当前会话 |
| `POST /api/auth/change-password` | 提交 `{ oldPassword, newPassword }` 修改密码 |
| `PUT /api/account/profile` | 提交 `{ name, phone, email }` 保存资料 |

业务 API（包括 `/api/ping`、ERP 和文件）默认需要登录，健康检查保持匿名。所有写请求（包括初始化、登录、退出和文件上传）均须携带 `X-CSRF-TOKEN` 请求头及对应 Cookie。客户端先请求 CSRF 令牌，登录成功后再获取绑定当前身份的新令牌；身份变化后不能沿用旧令牌，不自动重试写请求。

登录、当前账户和资料保存返回 `username`、`name`、`phone`、`email`、`lastLoginAt`、`expiresAt`；时间使用 ISO 8601。未初始化时登录及受保护接口返回 `409`，ProblemDetails 顶层 `code` 为 `initialization_required`；初始化重复请求或并发败者返回 `409`、`already_initialized`。前端保留安全的原目标地址，完成初始化和登录后回到该地址。

初始化、退出和改密成功返回 `204`。错误使用 ProblemDetails：输入或旧密码错误为 `400`，凭证或会话失效为 `401`，数据库不可用或管理员记录异常缺失为 `503`。初始化请求断线或服务错误后只重新查询状态，不自动重放写请求；查询已初始化不代表刚才的请求一定是赢家，应转到登录页确认。

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

在 Aspire Dashboard 的资源列表中查看状态、端点、控制台日志和追踪。终端诊断命令直接使用 PATH 中的 `aspire`；启动与停止见 [README](../README.md#启动并访问)。

从仓库根目录检查实际资源名、端点和就绪状态，再查看 API 日志与追踪：

```bash
aspire describe --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
aspire wait web --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
aspire otel logs api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
aspire otel traces api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

浏览器日志使用 `web-browser-logs` 子资源，需要 Chrome 或 Edge。打开受跟踪的浏览器后查看该资源的控制台日志：

```bash
aspire resource web-browser-logs open-tracked-browser --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
aspire logs web-browser-logs --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

资源名以 `describe` 输出为准。浏览器输出不在 `aspire otel logs web` 中。

### 启动和健康检查

- Aspire 的 `web-installer` 没有交互终端。安装参数使用 `--config.confirmModulesPurge=false`，允许 pnpm 11 自动重建因 store 路径等变化而不兼容的 `node_modules`，避免 `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY`；仍保留 `--frozen-lockfile`，不改变手工安装的确认行为。
- [AppHost 启动配置](../OpenERP.AppHost/Properties/launchSettings.json) 让 `localhost`、`127.0.0.1`、`::1` 绕过 HTTP 代理，避免本地 gRPC 被系统代理转发，不修改全局代理设置。
- [Vite 配置](../OpenERP.Web/vite.config.ts) 将浏览器的 `/api` 请求转发到 Aspire 注入的 API 地址；仅开发代理接受后端自签证书。
- SeaweedFS 的 Master/Volume 健康检查之外，AppHost 另用签名 S3 `ListBuckets` 检查等待存储就绪，再启动 API 建桶。这是项目自定义的启动保护，不保证建桶必然成功；存储后续失去健康也不会自动暂停已运行的 API。
- Development 环境的 `/health` 检查 PostgreSQL、Redis 和 SeaweedFS S3 连接；`/alive` 只检查进程存活。

### 更新运行中的代码

- **后端 C# 代码**：在 Dashboard 资源列表找到 `api`，打开操作菜单，选择 **Rebuild（重新构建）**。等待资源恢复健康后，再回到业务页面刷新并重试。该操作重新构建并启动 API；只点 Restart（重启）不能当作已经重新编译的证据。
- **前端代码**：Vite 开发服务器通常会通过 HMR 自动更新页面。前端已更新不代表 API 已加载新代码；若出现新表单仍收到旧接口错误，先检查 `api` 的日志与重建状态。
- **自动热重载**：只有对应 IDE Hot Reload 或运行时 watch 已启用时，才依赖它应用后端修改；普通启动不代表自动启用了后端热重载。
- **AppHost 编排或配置**：需要应用新的资源配置时，在原仓库或 worktree 中重新执行相同的启动命令；隔离实例使用 `aspire start --isolated`。编辑器启动的实例由编辑器管理重启。

仅一个资源的实现发生变化时，优先使用该资源的 Rebuild 或已有热重载流程，不重启整个 AppHost。终端操作可先用 `aspire describe` 查看资源；资源提供重建命令时使用 `aspire resource api rebuild`，随后 `aspire wait api` 等待就绪。若 Dashboard 没有 Rebuild 操作，先确认资源类型和启动方式，不把 Restart 视为等价替代。

### 开发数据与密钥

PostgreSQL、Redis 和 SeaweedFS 均使用持久容器与数据卷。PostgreSQL 挂载到 `/var/lib/postgresql`，Redis 和 SeaweedFS 挂载到 `/data`。卷名由 Aspire 根据应用身份与资源名生成，以当前环境为准；macOS Docker Desktop 的卷保存在其 Linux 虚拟机内，不在源码目录。

开发密码保存在 .NET User Secrets 中。只查看密钥文件位置、避免打印秘密值时使用：

```bash
aspire secret path --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

正常停止会保留持久资源；`aspire stop --force` 会删除持久资源，不能用于普通停止。保留数据卷时也应保留开发密钥及 `UserSecretsId`，避免密码不匹配。Redis 使用快照持久化，异常断电可能丢失最近的内存写入。

PostgreSQL `18-alpine` 和 Redis `8-alpine` 使用浮动大版本标签，小版本可能随镜像更新变化，本机缓存也会影响实际运行版本。SeaweedFS `4.53` 的普通发布镜像已基于 Alpine。[AppHost 镜像配置](../OpenERP.AppHost/AppHost.cs) · [SeaweedFS 发布构建](https://github.com/seaweedfs/seaweedfs/blob/4.53/.github/workflows/container_release_unified.yml) · [SeaweedFS Dockerfile](https://github.com/seaweedfs/seaweedfs/blob/4.53/docker/Dockerfile.go_build)

如果当前 checkout 已有 PostgreSQL `18` 创建的开发卷，首次切换到 Alpine 时请使用全新的默认 PostgreSQL 卷。例如，从新的 checkout 路径启动，Aspire 会为该路径创建默认卷。

## 使用 AI 开发工具

仓库代理规则由 [AGENTS.md](../AGENTS.md) 提供，专项流程位于 `.agents/skills/`。

仓库的 [.mcp.json](../.mcp.json) 与 [.vscode/mcp.json](../.vscode/mcp.json) 使用 `aspire agent init` 生成的 `aspire agent mcp` 配置。客户端与终端均直接使用 PATH 中的 `aspire` CLI，不额外套用 `dotnet tool run`。代理启动及其他生命周期操作遵循项目内 [Aspire Skill](../.agents/skills/aspire/SKILL.md) 及其路由的子 Skill。
