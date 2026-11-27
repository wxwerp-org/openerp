# 开发说明

贡献要求与协作规则见[贡献指南](../CONTRIBUTING.md)。

## 本地开发

### 环境准备

- 正式版 .NET **10** SDK，[global.json](../global.json) 选择已安装的最高 .NET 10 SDK。
- Node.js **26**，见 [.node-version](../.node-version)。
- pnpm **11.16.0**，见 [package.json](../OpenERP.Web/package.json)。
- PATH 中可直接调用的 Aspire CLI，版本与项目的 **17.0.0** 对齐；安装方式见 [Aspire 官方安装说明](https://aspire.dev/get-started/install-cli/)。
- 使用默认 `https` profile 时需启动 Docker Desktop 或 Aspire 支持的兼容容器运行环境。无 Docker 开发可使用 `native` profile。

在仓库根目录安装依赖并信任开发证书：

```bash
dotnet tool restore
aspire certs trust
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

本文直接调用 PATH 中的 `aspire`。`dotnet tool restore` 还原仓库本地工具（包括 EF Core 工具），不替代 PATH 中 Aspire CLI 的安装。首次信任证书可能需要系统确认，已信任后无需重复操作。

### 启动与停止

**无 Docker 开发**时，在仓库根目录执行：

```bash
aspire start --isolated --launch-profile native
```

`api` 和 `web` 作为本机进程运行，使用 SQLite、进程内 HybridCache 和本机对象存储。首次启动会在当前仓库或 worktree 下自动创建 SQLite 文件 `.openerp/native/openerp.db` 和附件目录 `.openerp/native/objects/`。

隔离模式会动态分配端口。在仓库根目录执行 `aspire describe web` 查看 Web 的 HTTPS 地址，或从启动输出打开 Aspire Dashboard，点击 `web` 的 HTTPS 地址。首次访问时设置管理员密码并登录。在同一仓库根目录执行 `aspire stop` 停止实例，再次执行上面的启动命令可继续使用原账号和附件。

**使用 Docker 开发**时，在仓库根目录启动默认的 `https` profile：

```bash
aspire start
```

仓库的 `aspire.config.json` 已指定唯一 AppHost，无需重复指定项目。本地开发使用固定的宿主端口，重启后地址不变：前端 `https://localhost:5173`、API `https://localhost:5201`、Aspire 控制台 `https://localhost:17000`（定义在 [AppHost.cs](../OpenERP.AppHost/AppHost.cs) 的 `DevPorts` 和 AppHost 的 `launchSettings.json`）。因此不要加 `--isolated`：它会随机化端口并隔离用户机密，每次地址都会变，管理员密码也会重新生成；确实需要同时运行第二个实例时才使用。普通后台启动用 `aspire start`；需要终端前台运行时可用 `aspire run`。

也可以在安装了 Aspire 扩展的 VS Code 中，选择仓库提供的 `apphost` 启动配置运行；需要 worktree 隔离时使用上面的 CLI 入口。

两种 profile 都可在 Dashboard 查看资源状态。管理员账号说明见[本地初始化与密码重置](#本地初始化与密码重置)。Dashboard 登录与应用管理员登录相互独立。

通过 `aspire start` 启动的后台实例，在同一仓库或 worktree 根目录运行 `aspire stop` 停止；关闭启动终端不会停止后台实例。终端前台 `aspire run` 按 `Ctrl+C` 停止；通过 VS Code 启动时使用编辑器的停止按钮。普通停止不要使用 `--force`，它会删除持久资源。

代码修改后见[更新运行中的代码](#更新运行中的代码)。数据卷与凭据的保留方式见[开发数据与密钥](#开发数据与密钥)。

### API 存储配置

默认 Aspire 启动和单容器发布使用 PostgreSQL、Redis 与 SeaweedFS S3。开发 AppHost 和独立运行的 API 均可选择数据库、缓存和对象存储。配置键可通过环境变量设置，用 `__` 表示层级分隔符。

| 配置键 | 取值与默认行为 |
| --- | --- |
| `NativeDataDirectory` | `native` profile 使用的数据目录，相对路径以 `OpenERP.AppHost/` 为起点，默认值为 `../.openerp/native`。 |
| `Database:Provider` | `PostgreSQL`（默认）或 `Sqlite`。 |
| `ConnectionStrings:openerp` | 所选数据库的连接字符串。PostgreSQL 使用 Npgsql 连接字符串，SQLite 使用 `Data Source=/absolute/path/openerp.db`，数据库文件的父目录须已存在。 |
| `Cache:Provider` | 开发 AppHost 专用，`Redis`（默认）编排 Redis 并注入连接。`Memory` 让 API 使用进程内缓存。 |
| `ConnectionStrings:redis` | 独立运行 API 时，设置该连接才启用 Redis 二级缓存。 |
| `ObjectStorage:Provider` | `S3`（默认）或 `Local`。 |
| `ObjectStorage:RootPath` | `Local` 模式的必填目录。相对路径以 API 内容根目录为起点，启动时创建该目录。 |
| `ConnectionStrings:seaweedfs` | `S3` 模式继续使用既有的 `Endpoint`、`AccessKey`、`SecretKey` 配置。单容器部署的凭据目录及轮换方式见[部署说明](deployment.md)。 |

开发 AppHost 按所选 provider 创建资源，保留 API 和 Web 的启动顺序。`native` profile 会按上面的默认位置准备 SQLite 连接和本机对象目录，并传给 API。也可通过表中的配置独立切换任一 provider。切换 provider 会连接所选数据库或对象存储，跨存储迁移现有数据需另行处理。

### 数据库迁移

修改共享 EF Core 模型时，从仓库根目录为两种数据库各生成一套迁移。将示例中的 `DescribeChange` 换成迁移名。设计时工厂读取环境变量及 `--` 后传入的配置。依据项目布局指定迁移工程与 API 启动工程。[EF Core 多 provider 迁移](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers)

```bash
dotnet ef migrations add DescribeChange \
  --project OpenERP.Migrations.PostgreSQL --startup-project OpenERP.Api \
  --output-dir Migrations --namespace OpenERP.Api.Migrations \
  -- --Database:Provider PostgreSQL

dotnet ef migrations add DescribeChange \
  --project OpenERP.Migrations.Sqlite --startup-project OpenERP.Api \
  --output-dir Migrations --namespace OpenERP.Migrations.Sqlite \
  -- --Database:Provider Sqlite
```

Development 环境启动 API 时会应用所选数据库的迁移。需手动升级到最新版本时，用 `dotnet ef database update` 替换对应命令的 `dotnet ef migrations add DescribeChange`，保留项目与 provider 参数，移除 `--output-dir` 和 `--namespace`，并通过 `ConnectionStrings__openerp` 配置目标数据库。API 在启动时分批补齐旧订单的日期检索值和旧店铺的名称检索值，中断重启后继续处理剩余行。单容器发布由入口脚本运行 PostgreSQL 迁移包，操作见[部署说明](deployment.md)。

## 代码导航

| 目录                                                   | 内容                              |
| ------------------------------------------------------ | --------------------------------- |
| [OpenERP.Web](../OpenERP.Web/)                         | Vue 前端与单元测试                |
| [OpenERP.Api](../OpenERP.Api/)                         | API、ERP 店铺目录、平台订单同步与数据存储 |
| [OpenERP.Persistence](../OpenERP.Persistence/)        | 共享 `AppDbContext` 与 EF Core 模型 |
| [OpenERP.Migrations.PostgreSQL](../OpenERP.Migrations.PostgreSQL/) | PostgreSQL 迁移 |
| [OpenERP.Migrations.Sqlite](../OpenERP.Migrations.Sqlite/) | SQLite 迁移 |
| [OpenERP.AppHost](../OpenERP.AppHost/)                 | 本地服务编排                      |
| [OpenERP.ServiceDefaults](../OpenERP.ServiceDefaults/) | 健康检查、遥测与服务发现          |
| [OpenERP.Api.Tests](../OpenERP.Api.Tests/)             | 后端单元测试和集成测试            |

前端基于 SpiderUI（Art Design Pro）框架，入口与目录约定见[前端结构与约定](#前端结构与约定)。[router/modules](../OpenERP.Web/src/router/modules/) 定义订单管理（订单列表）、店铺（授权店铺）、设置（用户管理、ERP 接入）等菜单与路由，其余为框架自带的模板、组件、示例演示页；[views](../OpenERP.Web/src/views/) 存放页面，[api](../OpenERP.Web/src/api/) 封装后端接口，[utils/http](../OpenERP.Web/src/utils/http/) 负责请求与会话。

## 构建与测试

工具版本以 [global.json](../global.json)、[.node-version](../.node-version)、[本地工具清单](../.config/dotnet-tools.json) 和 [package.json](../OpenERP.Web/package.json) 为准。

安装依赖后，在仓库根目录构建并运行测试：

```bash
dotnet build OpenERP.slnx --no-restore
dotnet test --solution OpenERP.slnx
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web build
pnpm --dir OpenERP.Web test
```

API 的版本信息在构建时注入：`OPENERP_BUILD_VERSION`、`OPENERP_BUILD_COMMIT`、`OPENERP_BUILD_TIME` 三个环境变量由[发布工作流](../.github/workflows/release.yml)在构建 API 前导出，分别写入 `AssemblyInformationalVersion` 与程序集元数据；本地未设置时兜底 `0.1.0-dev`。只读接口 `GET /api/about` 匿名返回 `version`、`commit`（7 位）和 `buildTime`（UTC ISO 8601），前端缺失时显示 `dev`。

### 后端集成测试资源与失败诊断

集成测试使用项目的 AppHost 模型，由 `AspireIntegrationFixture` 管理测试会话。fixture 初始化时启动 PostgreSQL 并等待 `openerp` 数据库健康。Aspire 17 同时准备 API 项目的构建产物。数据库与 CLI 测试可直接使用数据库，API 进程、Redis 和对象存储仍按需启动。需要存储的测试调用 `EnsureStorageStartedAsync`（获取存储连接时也会确保就绪）；HTTP 测试在异步初始化中调用 `EnsureApiStartedAsync`，并行准备存储和 Redis，待依赖就绪后启动 API。`StartWebAsync` 先准备 API，再启动前端安装器和前端。同步创建 HTTP 客户端不会隐式启动服务。

兼容的测试在同一 xUnit collection 内复用 PostgreSQL 服务器，数据库专用用例仍各自创建和删除独立数据库；共享 API 的用例保持串行。首次初始化与浏览器验收继续使用独立 fixture，避免共享账号、迁移和初始化状态。资源启动任务只执行一次，失败不会自动重试；已经启动的资源保留至 fixture 结束，因此全量运行中后续数据库用例可能与已启动的 API 共存。容器、密钥目录和存储卷属于本次测试会话，存储卷在测试内跨重启保留数据，最终随 fixture 清理。

认证并发测试先用真实 PostgreSQL 行锁确认操作进入等待队列，同时观察 CLI 或 HTTP 操作是否提前结束。默认每阶段锁等待最多 30 秒，真实子进程执行最多 90 秒；测试取消仍会提前终止等待。失败输出包含阶段、耗时、锁数量及操作结果，CLI 的退出码和已收集输出也会保留，子进程日志脱敏后交给 xUnit 的 `ITestOutputHelper`，随对应测试进入标准报告；不要在诊断中输出密码、stdin、私钥或完整连接字符串。失败后先结束测试持有的事务，再有界等待操作完成、取消并回收尚未结束的操作，最后释放连接和删除数据库；清理使用独立的 30 秒预算，事务释放与子进程回收各使用 10 秒子预算，主错误与清理错误同时报告。共享改密测试在释放观察连接后通过真实 CLI 恢复密码并撤销旧会话，避免晚到的改密请求再次改变共享账号。排障时先查看测试输出和 CTRF 报告，区分资源未就绪、操作提前退出、锁等待超时与清理失败，不以自动重跑掩盖失败。

### 前端结构与约定

前端位于 [OpenERP.Web](../OpenERP.Web/)，基于 SpiderUI（Art Design Pro）：Vue 3、Element Plus、Pinia（持久化）、vue-i18n、vue-router（Hash 路由）、SCSS 与 Tailwind CSS v4，构建使用 Vite。

- **页面与路由**：页面放在 `src/views/<模块>/<页面>/index.vue`；菜单与路由在 `src/router/modules/*.ts` 声明，`component` 写成相对 `views` 的路径（如 `/order/list`）。菜单由前端按角色过滤，不依赖后端菜单接口：管理员对应 `R_SUPER`，普通用户对应 `R_USER`，`meta.roles` 为空表示所有登录用户可见。菜单标题使用 `src/locales/langs/zh.json`、`en.json` 里的 `menus.*` 键，新增菜单时两个文件都要补，且键名不要与框架已有的 `menus.shop` 等冲突。
- **接口调用**：统一走 [utils/http](../OpenERP.Web/src/utils/http/index.ts)，写法为 `request.get<T>({ url, params })`，失败抛出带服务端 `detail` 文案的 `ApiError`；接口按领域放在 `src/api/*.ts`。令牌、CSRF 与刷新由 [session.ts](../OpenERP.Web/src/utils/http/session.ts) 处理：access token 只放内存，刷新值在 HttpOnly Cookie 中，业务写请求不自动重放。页面不要直接 `fetch`。
- **登录与会话**：路由守卫在首次进入时校验会话（`checkSession`），未初始化跳转 `/auth/initialize`，未登录跳转 `/auth/login` 并带 `redirect`；账号信息由守卫同步到用户 store。退出登录用 `userStore.signOut()`，它先通知服务端再清理本地状态。
- **业务逻辑**：与界面无关的取数、校验和推导放在 `src/utils`、`src/hooks/business`，例如 SignalR 实时连接（`hooks/business/useErpHub.ts`）。这些模块有单元测试，改动前先确认保护。
- **样式**：优先使用 Element Plus 组件和框架的主题变量（`--el-*`、`--default-box-color` 等），页面私有样式写在 `<style scoped>`，颜色引用变量而不是硬编码；需要全局调整时改 `src/assets/styles`。表格用 `ArtTable` / `ArtTableHeader`，其列配置类型为 `ColumnOption`。
- **自动导入**：`vue`、`vue-router`、`pinia`、`@vueuse/core` 的 API 与 Element Plus 组件由 `unplugin-auto-import`、`unplugin-vue-components` 自动导入，类型声明生成在 `src/types/import`。
- **开发代理**：[vite.config.ts](../OpenERP.Web/vite.config.ts) 把 `/api` 转发到 Aspire 注入的 `API_HTTPS` 或 `API_BASE_URL`，缺失时使用 `.env.development` 的 `VITE_API_PROXY_URL`。

### 页面布局与间距规范

业务页（下表中的页面）的面板间距**只由容器的 `gap` 决定**，页面里不再手写外边距。规则由 [PageLayout.spec.ts](../OpenERP.Web/tests/unit/PageLayout.spec.ts) 强制检查，违反会使单元测试失败。

- **全局间距变量**：`--page-gap: 12px`，定义在 [app.scss](../OpenERP.Web/src/assets/styles/core/app.scss)。需要改间距只改这一处。
- **根容器**：纵向堆叠的页面用 `page-stack`，需要撑满可视高度的表格类页面再加 `page-stack--fill`；横向并排用 `page-row`；统计块等分网格用 `page-grid`（列数用 `--cols` 指定，默认 4）。提示、统计、筛选卡片、表格卡片都作为这些容器的**直接子元素**，间距相同。
- **禁止手写外边距**：模板里不用 `mb-*`、`mt-*`、`my-*`、`m-*`，样式里不写 `margin`、`margin-top`、`margin-bottom`。需要内部留白用 `padding` 或 `gap`；左右方向的 `margin-left`、`margin-inline` 不受限制。框架的 `.art-table-card` 自带的 `margin-top` 在 `page-stack` 内已被抵消，不会与 `gap` 叠加。
- **筛选卡片**：`<ElCard class="search-card">`，它去掉了行内表单每一项默认的 18px 下外边距。
- **提示**：统一用 `AppNotice`（[app-notice](../OpenERP.Web/src/components/business/app-notice/index.vue)）：小字号、小图标、带边框和圆角，作为独立的一块呈现，`tone` 取 `info`、`success`、`warning`、`error`，右侧操作放在 `#action` 插槽。业务页不使用 `ElAlert`。页内的提示放在卡片外面，做 `page-stack` 的直接子元素。
- **汇总数字**：统一用 `AppStat`（[app-stat](../OpenERP.Web/src/components/business/app-stat/index.vue)）放进 `page-grid`：一行标签、一行 18px 数值、可选说明，不再用 `ElCard` 自制大卡片。
- **弹窗**：弹窗内容同样包在 `page-stack` 里，遵守同一套规则。
- **图标与多行文字并排**：左侧图标、右侧标题加多行说明（如 API权限绑定页的「功能介绍」卡片）时，图标必须靠上，与标题首行对齐，不要垂直居中。容器用 `display: flex; align-items: flex-start`，图标加 `flex: none` 固定尺寸；文字区变高时图标不跟着下移。图标与单行文字并排（按钮、标签、提示）仍可居中。
- **空状态**：没有数据且需要引导时，用向导卡片代替空表格（如授权店铺：绑定旺销王 ERP 密钥 → 读取全部店铺 → 使用各项功能）。
- **文案**：上游 ERP 的产品名称统一写“旺销王”，不使用旧称。该约束由 `PageLayout.spec.ts` 检查。

页面清单（新增业务页时补充到这里，并加入 `PageLayout.spec.ts`）：

| 页面 | 路由 | 文件 | 根容器 | 主要内容 |
| --- | --- | --- | --- | --- |
| 订单列表 | `/order/list` | `views/order/list` | `page-stack` | 筛选、提示、表格（左右铺满卡片、自然高度）；复选框与底部悬浮批量操作条、固定操作列（详情跳转订单详情页、删除）；「同步历史订单」跳转订单同步页 |
| 订单详情 | `/order/detail/:id`（隐藏路由，从订单列表的「详情」进入） | `views/order/detail` | `page-stack` | 标题卡、统计块、商品明细表，右侧基本信息、金额与支付、物流、状态、时间线 |
| 订单同步 | `/order/sync` | `views/order/sync` | `page-stack` | 提示、定时同步（管理员）、店铺列表与逐店同步进度 |
| 授权店铺 | `/shop/list` | `views/shop/list` | `page-stack` | 无店铺时向导；有店铺时平台标签页（角标为授权无效数）、筛选、表格、分页、详情弹窗 |
| 用户管理 | `/system/user` | `views/system/user` | `page-stack page-stack--fill` | 筛选、提示、表格；添加/编辑/查看弹窗 |
| API权限绑定 | `/system/erp-binding` | `views/system/erp-binding` | `page-stack` | 首次向导（输入、检测、保存）+ 配置内容；入口在顶部栏 |
| 登录、初始化 | `/auth/login`、`/auth/initialize` | `views/auth` | 框架登录版式 | 无面板，不适用本规范 |
| 个人资料、修改密码 | 头像菜单 | `components/core/layouts/art-header-bar/widget/ArtUserMenu.vue` | 弹窗 | 表单弹窗 |

框架自带的演示页（分析页、电子商务、模板中心、组件中心、功能示例、文章管理、结果页面、异常页面、运维管理）沿用 SpiderUI 的版式，不受本规范约束。

### 前端静态检查

ESLint 使用 [eslint.config.mjs](../OpenERP.Web/eslint.config.mjs)，Prettier 规则通过 `eslint-plugin-prettier` 一并检查，Prettier 配置见 [.prettierrc](../OpenERP.Web/.prettierrc)，因此不再有单独的格式检查脚本。

```bash
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web fix
```

`lint` 只读检查，`fix` 自动修复可修复的问题（包括排版）。遇到 lint 报错或警告，按对应规则的官方文档修正成因，不用行内禁用或放宽配置绕过检查。

类型检查为 `pnpm --dir OpenERP.Web typecheck`（`vue-tsc --noEmit`）。目前仍有 SpiderUI 自带泛型组件（`art-form`、`art-search-bar`、`art-table`）和 `SidebarSubmenu` 的 10 处类型报错，属于框架遗留问题，新增代码不应再增加报错。

### 前端视觉验收

框架已不再使用旧的视觉探针和设计规范原型。改界面后，用浏览器打开页面核对，并在 PR 中附截图。

### 测试环境

后端使用 xUnit v3、原生 .NET 10 Microsoft.Testing.Platform 和 Aspire.Hosting.Testing，配置见 [global.json](../global.json) 与[测试项目](../OpenERP.Api.Tests/OpenERP.Api.Tests.csproj)。订单 PostgreSQL 与文件存储测试共用隔离 Aspire 测试宿主，需要容器环境和受信任的开发证书。宿主自行启动和清理资源，订单用例另建并清理独立数据库，无需 `OPENERP_TEST_POSTGRES`。回归用例见 [OpenERP.Api.Tests](../OpenERP.Api.Tests/)。

前端单元测试位于 [tests/unit](../OpenERP.Web/tests/unit/)，使用 Vitest、Vue Test Utils 和 jsdom，配置见 [vitest.config.ts](../OpenERP.Web/vitest.config.ts)（别名、自动导入与 `vite.config.ts` 保持一致）。页面测试用桩替换依赖全局 store 与国际化的 `ArtTable`、`ArtTableHeader`，只验证页面收到的数据与交互。依赖和执行脚本以 [package.json](../OpenERP.Web/package.json) 为准。

模拟接口失败时不要对 `vi.fn()` 使用 `mockRejectedValue`：`vi.fn` 会给返回的 Promise 挂记录回调，被拒绝的 Promise 会因此变成未处理的拒绝，导致用例失败。请用普通函数返回 `Promise.reject(...)`，页面测试里已有写法可参照。

[WebInstallerTests](../OpenERP.Api.Tests/WebInstallerTests.cs) 使用实际 AppHost 安装参数，在临时目录执行 pnpm 验证无 TTY 的依赖重建与冻结锁文件行为。需要 PATH 中有 Node.js 和 pnpm；fixture 只使用本地依赖，不连接 ERP，也不修改开发目录的 `node_modules`。

前端重构后，旧的 Playwright 浏览器用例、视觉探针和 `playwright.config.ts` 已删除，尚未重建。依赖这些用例的 [BrowserAcceptanceTests](../OpenERP.Api.Tests/BrowserAcceptanceTests.cs) 目前不可用，不要启用 `OPENERP_RUN_BROWSER_TESTS`；[前端工作流](../.github/workflows/ci-web.yml)里仍引用旧的 `format:check`、`typecheck` 脚本和 Playwright 任务，需要随浏览器用例一并调整。

### GitHub Actions

[后端 PR 检查](../.github/workflows/ci-backend.yml)在 PR 改动后端项目、共享构建输入、Node.js/pnpm 工具链文件或该 workflow 时触发，按照 [Aspire 官方 CI 指导](https://aspire.dev/testing/testing-in-ci/)在 Ubuntu 上还原、构建并无过滤执行整个 solution。容器由既有 Aspire 测试夹具启动和清理，YAML 不自行编排容器；Node.js/pnpm 用于既有 `WebInstallerTests`，HTTPS 信任用于既有集成测试。构建、容器启动、测试断言或清理失败会使 job 失败。

后端在锁定还原后、构建前运行 `python3 .github/nuget/check-graph.py`。它读取解决方案内每个项目的 `obj/project.assets.json`，逐个检查目标框架及 RID 的已解析包。直接或传递引入的 .NET 产品线组件解析为 11 或更高主版本时，日志列出项目、目标、包名及实际版本。可用 `dotnet nuget why OpenERP.slnx <包名>` 追查引入路径。[NuGet 依赖解析](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution#dependency-resolution-with-packagereference) · [.NET SDK 依赖链查询](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-why)

[包名清单](../.github/nuget/dotnet-packages.txt)按微软发布表中与 Runtime 同版的包（含该版本的预发行组件）、Platform Extensions 和本项目配套的 Npgsql EF provider 确定。平台运行包按同系列匹配，其余按包 ID 区分，例如 `System.IO.Hashing` 随 Runtime 发布，`System.IdentityModel.Tokens.Jwt` 随 IdentityModel 发布。微软发布新包 ID 或项目迁移产品线时，按发布清单同步更新。CI 从仓库读取清单，可离线检查已还原的图。[.NET 10.0.15 包表](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.15/10.0.15.md#packages-updated-in-this-release) · [.NET 11.0.0 包表](https://github.com/dotnet/core/blob/main/release-notes/11.0/11.0.0/11.0.0.md#packages-updated-in-this-release) · [Platform Extensions 包表](https://github.com/dotnet/extensions/releases/tag/v11.0.1)

测试使用运行器原生 `--zero-tests-policy strict` 拒绝零用例。xUnit 直接生成 CTRF 报告 `backend.json`，GitHub Actions 将测试结果目录原样上传为 `backend-results-<attempt>`，可从对应运行下载。CI 未提供真实 ERP 凭证或隔离浏览器开关时，相应用例跳过，单独报告。完整后端套件包含真实容器集成测试，执行耗时不能按纯单元测试估算。

[前端检查](../.github/workflows/ci-web.yml)在 PR 改动 Web 项目、发布宿主输入、共享构建配置或该 workflow 时触发，也在每次推送 `main` 时运行。它执行 lint、类型检查、单元测试与构建，依赖安装成功且运行未取消时，一项失败后仍执行其余检查。浏览器 job 只在 `workflow_dispatch` 时执行，手动运行失败仍会传递到结果汇总；失败时在运行页下载 `playwright-report-<attempt>` 产物，用 `pnpm --dir OpenERP.Web exec playwright show-report <下载的 zip>` 查看失败用例的截图与 trace。浏览器 job 的前序步骤失败时会尝试上传已生成的报告。

前端 workflow 的 `web-hosting` job 在独立目录验收单容器发布形态，覆盖 HTTPS 入口、管理员登录、附件读写和容器重建。本地 Aspire 开发及后端测试仍使用各自的服务资源。

在仓库根目录使用前述固定版本的 Aspire CLI、Docker、yq v4（Mike Farah）、Python 3 和 OpenSSL，可以复现发布验收：

```bash
dotnet tool restore
dotnet restore OpenERP.slnx --locked-mode
aspire do build-openerp --non-interactive
aspire publish --non-interactive -o "$PWD/aspire-output/generated"
yq --from-file deploy/compose.yq ./aspire-output/generated/docker-compose.yaml > ./aspire-output/docker-compose.yaml
python3 deploy/tests/smoke.py --compose ./aspire-output/docker-compose.yaml
```

默认验收本次构建的 `openerp:local`。已有两个本地版本时，通过 `--image <旧镜像> --upgrade-image <新镜像>` 检查升级后数据能否读回。Release 工作流先验收本次组装的镜像，再推送同一个镜像并上传版本化配置，服务器操作见[部署说明](deployment.md)。

### 发布基础镜像

发布入口是 GitHub Release（含预发布）。[Release 工作流](../.github/workflows/release.yml)根据本次提交的 [base.Dockerfile](../deploy/base.Dockerfile) 计算 SHA-256，在私有 GHCR 包 `openerp-base` 中查找 `linux-amd64-<配方哈希>`。首次使用该配方时，Actions 自动构建、验证并推送基础镜像。之后使用相同配方的 Release 直接取得已有镜像的 digest。组装业务镜像时使用该 digest，Release 部署附件只引用最终的 `openerp` 镜像。

升级 PostgreSQL、ASP.NET Runtime、Redis、SeaweedFS 或 Caddy 时，在基础配方中更新对应的上游标签和 digest，再按平时的流程发布 GitHub Release。操作系统软件包也在构建基础镜像时安装，已发布的 base 保留当次安装结果。API、迁移包、静态页面、Caddy 配置及入口脚本随业务镜像发布。PR 和本地的 `aspire do build-openerp` 直接从工作树中的配方构建本地 base，可在私有包首次创建前完成烟测。

## 用户、权限与会话

唯一管理员 `admin` 管理用户、系统接入、全局目录与定时同步，并能读取所有店铺。员工使用 `User` 角色，可以从店铺列表去旺销王授权新店。员工的目录、平台选项、订单和手动同步范围由服务端读取当前分配。

`UserShopAssignments` 以用户 UUID、平台和 SID 为复合键。迁移为存量员工建立空范围。管理员在用户管理中选择已同步到本地目录的店铺，可以保留或撤销暂时下架的已有分配。归属独立于目录行，因此历史订单继续可查，同平台同 SID 恢复后沿用原分配。删除员工会在同一事务中清理归属和会话。

管理员用户名固定。姓名必填（最多 20 个字符），手机号可选（最多 20 个字符），邮箱可选并校验格式；手机号、邮箱仅作为个人资料。菜单中的资料与最近登录时间来自数据库，保存资料不会改变最近登录时间。

### 本地初始化与密码重置

Development 环境启动 API 时会应用[所选数据库的迁移](#数据库迁移)。API 每次启动生成临时 RS256 签名密钥。空库首次启动后，在应用的 HTTPS 页面设置管理员密码，再使用 `admin` 登录。已有登录的浏览器可以在 API 重启后使用持久刷新 Cookie 静默恢复。正式环境的配置、初始化和升级见[部署说明](deployment.md)。

启动时只在管理员行缺失时创建占位行。设置密码通过数据库条件更新完成，并发请求只能有一方成功。初始化成功记录 UTC 时间、连接来源 IP 和 User-Agent，不记录密码，也不签发令牌或更新最近登录时间。

本地忘记密码时，构建后从仓库根目录运行下列命令。为重置进程单独配置 `ConnectionStrings__openerp`，使用 SQLite 时还需设置 `Database__Provider=Sqlite`。

```bash
dotnet OpenERP.Api/bin/Debug/net10.0/OpenERP.Api.dll reset-admin-password
```

命令以隐藏输入提示新密码和确认密码，并撤销管理员在所有设备上的登录。正式环境的操作与自动化方式见[管理员密码重置](deployment.md#重置管理员密码)。

### 会话规则与接口

API 使用 RS256 签发 Bearer access token，`sub` 为用户 UUID，`role` 为 `Administrator` 或 `User`。令牌最长有效期为 20 分钟，且不超过刷新期限。验证时不设时钟宽限。前端只在内存中保存 access token，每 5 分钟主动尝试刷新。刷新期限自登录起固定为 24 小时，刷新不延长期限或轮换凭证。刷新值只通过 `HttpOnly`、`Secure`、`SameSite=Strict` 的持久 Cookie 传递，Cookie 仅发往当前主机的 `/api/auth` 路径。服务端在数据库的 `RefreshTokens` 表中保存刷新值的哈希。浏览器关闭或 API 重启后仍可使用有效的刷新凭证。

普通退出撤销当前刷新凭证。网页修改密码撤销本人所有会话，命令行重置撤销管理员的所有会话。服务器逐请求核验 JWT 对应的登录标识仍有效，因此已签发的 access token 也会随对应刷新凭证撤销而失效。网页修改密码必须提供旧密码；命令行重置直接设置最终密码。已受理的订单同步继续执行。

前端遇到网络错误或 `503` 时不能据此退出登录；收到 `401` 后先通过刷新接口确认登录是否仍有效。刷新成功后，读取请求最多重试一次；业务写请求不自动重放，以避免网络中断或令牌过期时重复执行操作。数据库不可用等服务故障应作为服务错误处理，不能当作用户已退出。

| 接口 | 用途 |
| --- | --- |
| `GET /api/auth/csrf` | 匿名获取 `{ requestToken }` 及对应 CSRF Cookie |
| `GET /api/auth/status` | 匿名查询 `{ initialized }`，响应禁止缓存 |
| `POST /api/auth/initialize` | 未初始化时提交 `{ newPassword }` 设置管理员密码 |
| `POST /api/auth/login` | 提交 `{ username, password }` 登录，返回 `{ accessToken, accessTokenExpiresAt, account }` |
| `POST /api/auth/refresh` | 不需要请求体；刷新凭证有效时返回 `{ accessToken, accessTokenExpiresAt, account }` |
| `GET /api/auth/me` | 查询当前账户 |
| `POST /api/auth/logout` | 不需要请求体；撤销当前刷新凭证 |
| `POST /api/auth/change-password` | 提交 `{ oldPassword, newPassword }` 修改密码 |
| `PUT /api/account/profile` | 提交 `{ name, phone, email }` 保存本人资料 |
| `GET /api/users`、`GET /api/users/{id}` | 管理员读取用户列表或指定用户详情 |
| `POST /api/users` | 管理员创建普通用户 |
| `GET /api/users/{id}/shops`、`PUT /api/users/{id}/shops` | 管理员读取或整体替换员工的店铺键数组，每项包含 `platform` 和整数 `sid`。提交空数组撤销全部分配 |
| `PUT /api/users/{id}`、`DELETE /api/users/{id}` | 管理员编辑或软删除员工账号 |

前端通过应用同源的 HTTPS `/api` 地址访问后端。初始化、登录、刷新和退出要求 `X-CSRF-TOKEN` 请求头及对应 CSRF Cookie，先请求 `/api/auth/csrf` 取得令牌。防伪校验使用所配置数据库中的 Data Protection 密钥。Bearer 认证的业务写请求，包括密码修改、资料保存和文件上传，无需附带 CSRF 令牌。业务 API（包括 `/api/orders`、ERP 与文件）默认需要登录，`/api/about` 与健康检查保持匿名。

`account`、`GET /api/auth/me` 和资料保存响应包含 `id`、`role`、`username`、`name`、`phone`、`email`、`lastLoginAt`、`expiresAt`，时间使用 ISO 8601。这里的 `expiresAt` 表示 24 小时刷新期限；access token 的到期时间单独由 `accessTokenExpiresAt` 返回。未初始化时登录及受保护接口返回 `409`，ProblemDetails 顶层 `code` 为 `initialization_required`；初始化重复请求或并发败者返回 `409`、`already_initialized`。前端保留安全的原目标地址，完成初始化和登录后回到有权访问的页面。普通用户访问用户管理、ERP 配置或定时同步设置接口返回 `403`。

普通用户改名后，旧用户名立即可用。删除时保留原行与 UUID 并撤销该账号的登录，满 7 天后可重新分配该用户名。新建同名账号获得独立 UUID。

初始化、退出和改密成功返回 `204`。错误使用 ProblemDetails：输入或旧密码错误为 `400`，凭证或登录标识失效为 `401`，数据库不可用或管理员记录异常缺失为 `503`。初始化请求断线或服务错误后只重新查询状态，不自动重放写请求；查询已初始化不代表刚才的请求一定是赢家，应转到登录页确认。

## ERP 接入与接口

上游地址固定为 `https://v3-api.wxwerp.com`。管理员在账户菜单「系统设置」打开的 ERP 接入页填写或更换 Token。API 使用 ASP.NET Core Data Protection 保护 Token，并将其保存到所配置的数据库。备份数据库时一并保留 Data Protection 密钥表。页面只显示保存状态。验证凭证与店铺权限时，以实际 ERP 请求的结果为准。

| 接口 | 行为 |
| --- | --- |
| `GET /api/erp/order-sync-availability` | 已登录用户查询 `configured` 和 `orderSyncEnabled`，供授权店铺页判断 ERP 是否已配置 |
| `GET /api/erp/config` | 管理员查询配置记录是否存在，返回 `configured`、`baseUrl` 和 `orderSyncEnabled`。查询失败返回 `503` |
| `PUT /api/erp/config` | 管理员提交 `{ accessToken }` 保存或更换 Token，成功返回 `204`。下一次读取凭证的操作使用新值 |
| `GET /api/erp/shops?page=1&pageSize=20&platform=&status=&keyword=` | 按账号范围分页读取本地目录，每页 1–100 条。员工可查看所属店铺的状态、失败原因和检查时间。管理员额外获得 `summary`，可按 `status=valid/invalid/unknown` 筛选 |
| `GET /api/erp/platforms` | 返回当前账号可见的本地店铺涉及的平台（名称、展示名、图标、是否生产可用、店铺数）；平台元数据来自上游同步，不在本地写死 |
| `GET /api/erp/shop-sync` | 仅管理员可读取。返回店铺同步的当前或最近一次状态（运行中、开始/完成时间、总数、已检查、有效/无效/未知、错误、单调递增的 `version`） |
| `GET /api/order-sync/settings` | 仅管理员在 Development / Production 可用；返回定时同步的 `enabled`、`shopSids` 和只读 `intervalMinutes` |
| `PUT /api/order-sync/settings` | 仅管理员在 Development / Production 可用；保存启用状态和店铺选择；启用时至少选择一家店铺 |
| `GET /api/order-sync/settings/shops` | 仅管理员在 Development / Production 可用。返回已实现订单同步的平台下的全部授权店铺。本地数据库读取失败返回 `503` |
| `GET /api/order-sync/availability` | 已登录用户查询 ERP 是否已配置（`configured`）、`orderSyncEnabled` 和已实现订单同步的平台列表 |
| `GET /api/order-sync/shops?page=1&pageSize=20` | 分页读取可见的授权店铺（编号、名称、平台、区域），并用 `syncSupported` 标明该平台是否已实现订单同步。管理员看到全部，员工只看到分配给自己的店铺 |

店铺目录同步和订单同步都通过 scoped `IErpTokenStore.GetAsync` 读取并解密 Token；读取失败由存储层返回 `OpResult`。配置状态只检查记录是否存在，因此旧密文损坏时管理员仍可查看配置并替换凭证。订单同步不保存任何平台凭据，店铺只用 Sid 标识。

`IErpClient` 的每个上游方法都显式接收不可变的 `ErpAccessToken`。同一操作的分页和并发状态查询共用该快照。`WxwErpClient` 只处理 HTTP 和上游响应，并发期间各请求无需访问凭证存储。参见 [EF Core 对 DbContext 并行使用的说明](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)。

本地店铺接口查询所配置的数据库，数据库暂时不可用时返回 `503`。旧 `/api/erp/authorization/*` 接口、授权快照与事件表已删除。实现见 [ShopDirectoryEndpoints](../OpenERP.Api/Erp/ShopDirectoryEndpoints.cs)、[ShopSyncJob](../OpenERP.Api/Erp/ShopSyncJob.cs)、[ErpHub](../OpenERP.Api/Erp/ErpHub.cs) 和 [OrderSyncEndpoints](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

### 本地店铺目录与授权状态

上游只提供店铺基础信息与单店 `isTokenOk` 布尔状态，没有 scope、有效期、取消授权、授权历史或授权回调。OpenERP 因此维护**本地店铺目录**，不冒充上游授权历史：

- `Shops`：完整本地店铺数据，按上游 `sid` upsert；上游本轮不再返回的店铺删除。字段包括名称、平台、区域、成员 ID、`IsTokenOk`、`TokenError` 与 `SyncedAt`。
- `ShopPlatforms`：上游平台目录的展示元数据（展示名、图标、生产可用标记）；平台筛选的店铺数从 `Shops` 聚合。
- `ShopSyncStates`：单行同步状态（最近开始/成功时间、错误、总数与有效/无效/未知计数、单调递增的 `Version`），不是店铺数据本身。
- 同步在 Development / Production 启动后延迟首刷，之后按 `ShopSync:IntervalMinutes`（默认 `60`，允许 `1`–`1440`）执行；页面也可通过 SignalR 手动触发。同一时间只运行一轮，重复触发返回冲突。
- 同步流程：全量读取上游店铺 → 读取平台目录 → 并发 `4` 逐店读取 `get_status` → 一次 `SaveChanges` 写入店铺、平台与同步结果。单店状态失败只把该店标为未知并记录 `TokenError`，不阻塞其他店铺。
- 同步状态带单调递增的 `Version`：开始、完成、失败时递增，进度沿用开始版本。`/api/erp/shops` 先读状态行再读店铺行，因此 `summary.SyncVersion` 不会把旧数据标成最新；客户端只应用版本不低于当前值的状态，并在状态版本大于已加载目录版本时刷新列表。
- 上游店铺列表或平台目录读取失败时**不修改店铺数据**，只更新 `LastError`；页面继续展示上次成功数据。wxw 暂时离线时，本地查询、筛选、分页、详情仍可用。
- 授权状态只有三档：`IsTokenOk=true` → 有效，`false` → 无效，`null` → 未知。不提供“授权中/待授权/已取消/即将过期”，也不记录本地授权操作或历史。
- 订单同步直接使用授权店铺目录里的店铺和 Sid；平台是否已实现订单同步由 `PlatformOrderSources` 决定，未实现的平台照常列出，但不能同步（见下文订单同步边界）。

### SignalR 同步

- 单 Hub：`/api/hubs/erp`。普通读取（订单列表、店铺目录、平台、配置、店铺同步状态）继续使用 HTTP；只有长时同步与进度使用 SignalR。
- 浏览器通过 `access_token` 查询参数连接 Hub。JWT 中间件只对 `/api/hubs` 路径读取该参数，其他路径忽略，避免 token 泄漏到普通接口。
- 管理员方法 `StartShopSync()` 返回 `accepted`、当前状态与错误；服务端事件 `ShopSyncUpdated(ShopSyncStatus)` 在管理员连接建立、同步开始、每 10 家店检查、完成或失败时向管理员推送。状态带单调递增 `version`，客户端据此丢弃迟到的 HTTP/invoke 旧快照。
- 订单同步页通过 `StartOrderSync(sid)` 启动单店同步，返回 `accepted`、该店状态与错误。「全部同步」通过一次 `StartAllOrderSync()` 命令读取当前账号可见、且平台已实现订单同步的授权店铺，未实现平台的店铺被忽略，返回本次受理和跳过的店铺数。
- 单店命令在读取状态或受理前检查 ERP Token 已配置、按当前分配检查 SID、确认店铺仍在授权店铺中，并拒绝未实现订单同步的平台。越权返回 `accepted: false` 和空状态。两种手动订单同步命令都为员工的当前连接订阅本次可同步店铺的进度，因此连接建立后新分配的店铺也能在原连接收到终态。受理后关闭页面或断开连接，任务继续执行。
- 连接或重连时，`OrderSyncSnapshot` 提供当前进程的 `epoch` 和可见店铺的最近状态。管理员接收全部进度，员工连接按当次分配加入各自的店铺组，重连时重建。手动与定时任务通过 `OrderSyncUpdated` 推送带版本的状态，包含阶段、进度、入库计数和跳过原因。成功或部分成功后，页面按当前筛选刷新本地订单。
- 客户端使用 `withAutomaticReconnect`；access token 由 `accessTokenFactory` 每次读取最新值，配合会话刷新处理 20 分钟 JWT 到期。连接断开时页面仍可浏览本地数据，但同步按钮不可用。
- Hub 映射开启 `CloseOnAuthenticationExpiration`：JWT 到期即关闭连接，客户端用刷新后的 access token 重连，避免连接无限期沿用旧凭证。
- `StartShopSync`、`StartOrderSync` 和 `StartAllOrderSync` 执行前按当前数据库会话校验一次登录状态；会话已撤销时拒绝命令并断开连接。这是官方文档给出的“在 Hub 方法内按当前数据校验”补偿，不是逐消息重新认证；普通 HTTP 接口仍按请求执行完整会话校验。
- 撤销分配后，新 HTTP 请求与同步命令使用数据库中的最新范围。已有连接的进度组成员资格在正常断线或 JWT 到期重连时更新。撤销会话后，新的受保护 HTTP 请求返回 `401`。
- 当前假设单个 API 实例；多实例部署需要 SignalR backplane，非本期范围。

## 本地订单列表

订单列表读取数据库中的订单快照，使用已认证的 `GET /api/orders`，支持 `shopSid`、`platform`、`number`、`status`、`createdFrom`、`createdTo` 和分页。它按主单计数和翻页，每页默认 20 条。订单号片段和状态同时指定时，两个条件须命中同一个平台订单；返回整张主单上的全部号码，并标出命中项。行数和件数均按整张主单计算。店铺名称来自本地授权店铺目录，订单所属店铺已从目录移除时，仍可通过 SID 展示和筛选。

订单列表按来源创建日期（`YYYY-MM-DD`）筛选，含起止日，默认展示全部历史。本地保留了平台返回的时间原值。像 `08-09-2025` 这样有两种有效解释的日期按未知处理。有效的创建时间按倒序排列，缺少有效时间的订单排在末尾，指定日期范围时排除。创建和出库时间按来源的日期和精度显示。筛选及分页无需访问平台。

## 订单同步边界

### 平台接口调用流程

**核心方法：** 一切调用平台开放 API 的功能（订单同步、面单打印……）都走 `IPlatformAgentApiClient.InvokeAsync(平台, 店铺Sid, 平台API名, 参数, AccessToken)`，后端不保存任何平台密钥。新功能先在 `apiSchemas` 里查平台 API 的入参和出参，再照下面的流程调用。

**调试前提：先绑定 AccessToken。** 没有绑定时所有平台调用都会失败。未绑定时请二选一，之后的开发、联调和验证交给 AI：

1. 把旺销王 AccessToken 直接告诉 AI，由 AI 绑定；
2. 自己登录后打开 `/system/erp-binding`（本地为 `https://localhost:5173/#/system/erp-binding`），按向导输入、检测、保存。

AI 不会也不应该去数据库、容器环境变量里翻 Token；dev 管理员密码同理，由开发者自己登录。

**改完代码怎么看效果：** 不需要手动打包。前端改完同步即热更新；后端改完执行 `accept.cmd api`，会自动编译检查并重启，然后刷新页面即可。

订单同步不使用旺销王自己的订单接口，也不在本系统保存任何平台凭据，而是通过旺销王的**平台接口**（Open API 里的「平台专属 / 平台接口」，`/open-api/agent/{平台}/invoke`）调用平台自己的开放 API。旺销王托管店铺的平台授权与应用密钥，调用方只提供店铺 Sid。接口说明见旺销王仓库 `docs/规范文档/开放API平台专属分类接入指南.md`，各平台 API 的入参、出参在 `wxw.Api/wwwroot/open-api/apiSchemas/platform/<平台>/<API 名>.json`，目录在 `platform-aliexpress-apis.json`。

1. 管理员在「ERP 接入」保存旺销王 Open API 的 AccessToken。该账号须有平台接口（`openApi`）额度，且店铺已在旺销王完成平台授权。
2. 订单同步页的店铺来自授权店铺目录（`Shops` 表，Sid 是旺销王的店铺编号）。平台已注册 `IPlatformOrderSource` 的店铺可同步，其余只列出并禁用按钮。
3. 同步任务按 Sid 取出目录里的店铺（平台、名称），再按平台名称找到对应的订单来源，调用 `ReadOrdersAsync`。
4. 订单来源通过 [PlatformAgentApiClient](../OpenERP.Api/Erp/PlatformAgentApiClient.cs) 调用 `POST {旺销王地址}/open-api/agent/{平台}/invoke`，请求头带 `AccessToken`，请求体为 `{ "sid": 店铺编号, "api": "平台 API 名称", "method": 可选, "params": { 平台参数 } }`。
   - `params` 的所有值必须是字符串；平台要求对象的参数（例如 `param_aeop_order_query`）先序列化为 JSON 文本再传。
   - 响应为 `{ "success": true/false, "message": 失败原因, "content": 平台原始响应 }`。`success=false` 时按 `message` 映射为中文提示（Token 无效、没有平台接口权限、店铺不存在或无权访问、店铺尚未授权等），不透传原始响应；`success=true` 时取 `content` 交给订单来源解析。
5. AliExpress 订单来源（[AliExpressOrderSource](../OpenERP.Api/Orders/Sources/AliExpressOrderSource.cs)）：
   - 调用 `aliexpress.trade.seller.orderlist.get`，`param_aeop_order_query` 带 `current_page`、`page_size`（50）、`create_date_start`、`create_date_end`（最近 180 天，格式 `yyyy-MM-dd HH:mm:ss`，平台要求美国太平洋时间），按 `total_page` 翻页。
   - 真实响应（2026-11-25 用店铺 156764 核对）是 `content.aliexpress_trade_seller_orderlist_get_response.result`，其中 `target_list` 和每个订单的 `product_list` 都再包了一层：`target_list.aeop_order_item_dto[]`、`product_list.aeop_order_product_dto[]`（文档里写的是直接数组）。解析会自动展开只含一个数组的包装对象。每个订单已包含商品行（`product_id`、`child_id`、`sku_code`、`product_count`），因此不再逐单调用详情接口。`result.success=false` 或 `error_response` 使整个任务失败，缺订单号、缺商品行或数量无效的订单计入「跳过」。
   - 订单标识为 `AliExpress:店铺编号:平台订单号`，时间保留平台返回的原值，来源标记为 `AliExpress`。
6. 订单快照交给 `OrderStore` 保存，步骤与其他平台相同。新增平台时实现 `IPlatformOrderSource`（需要时再给 `PlatformAgentApiClient` 增加平台的调用约定）并在 `Program.cs` 注册。

验证状态：订单同步（2026-11-25）和面单打印（2026-11-26，见 [订单打印面单](order-label-printing.md)）都已用真实 AccessToken 和店铺联调过；平台响应字段以真实返回为准，新接口先小范围试一次再写解析。

### 订单同步边界

- 订单同步抽象为 `IPlatformOrderSource`：每个平台一个实现，读取某家店的订单并转换为统一的订单快照，同步任务、进度和定时调度只依赖该契约，不关心各平台不同的订单结构。`PlatformOrderSources` 按平台名称解析实现，目前内置 AliExpress。
- 店铺沿用授权店铺目录里的 Sid，订单、员工分配、定时同步都以「平台 + Sid」识别店铺。订单主单以 `平台:店铺编号:平台订单号` 标识；平台子单和商品行关联主单，订单时间保留平台原值及来源。
- API 在 Development 启动时应用 EF Core 迁移。
- 完整读取并校验列表和详情后才写库；读取失败时不保存本批结果，数据库保存失败时整批回滚。
- 归属异常的订单整单跳过，任务显示部分成功和跳过原因。只刷新本批通过校验的订单，未返回或被跳过的已有订单会保留；已接受订单的子项按本次结果更新。
- 同步任务状态保存在 API 内存中，每店保留最近一次任务。手动和定时同步共用 4 个店铺执行位置，同一店铺同时只接受一个任务。其余已受理店铺在页面显示「等待同步」，获得空位后读取订单。各店分别保存，写入时串行更新主单。刷新网页可恢复状态，API 重启后状态丢失；手动同步需重新触发，定时同步会在下一周期重跑，已保存的订单仍在数据库中。
- 订单列表按每页 50 条读取，达到平台返回的总数或末页即停止。平台使用页码分页，没有一致性快照：扫描期间订单进入或退出范围时仍可能漏读，下一次同步会补上。
- 订单详情逐单读取，所有店铺在一个 API 进程内各自最多并发 4 个详情请求，按原列表顺序汇总进度。单店完整读取并校验后，EF Core 批量写入并一次提交。
- 本地保存通过 ASP.NET Core `HybridCache`（进程内一级、Redis 二级）复用已落库订单的 SHA-256 指纹。每次先批量读取 PostgreSQL 主单 ID 和版本；当前版本与完整订单内容都命中时，直接计为「未变」。新单和未命中的订单走正常保存或明细比对。所有订单写入入口都须在业务数据变化时于同一事务内更换主单版本，包含仅子单或商品行变化的情况。缓存于保存成功后填充。缓存过期或丢失可从数据库重新比对，订单详情仍逐单实时读取。

相关实现：[平台订单来源](../OpenERP.Api/Orders/Sources/PlatformOrderSource.cs)、[平台接口客户端](../OpenERP.Api/Erp/PlatformAgentApiClient.cs)、[任务状态](../OpenERP.Api/Orders/OrderSyncJobs.cs)、[订单保存](../OpenERP.Api/Orders/OrderStore.cs)。

## 定时同步边界

- 定时同步与手动同步同样只在 Development / Production 提供。订单同步页（`/order/sync`）中的「定时同步」卡片只配置是否启用和选择店铺，不提供周期配置。
- 周期来自 API 配置 `OrderSync:SchedulerIntervalMinutes`（默认 `30`，允许 `1`–`1440` 分钟），启动时校验；修改后需重启 API，保存设置本身不需要重启。每轮受理后等待一个周期再尝试下一轮；若上一轮仍在运行，本轮跳过并等下一周期。启用后首次执行同样等待一个周期。
- 设置保存在 PostgreSQL 的 `OrderSyncSettings` 单行表，使用 UUIDv7 主键和独立的数据库单行约束。保存后 API 唤醒调度器重新读取设置；启用后首次执行仍等待一个完整周期。
- 启用时必须至少选择一家店铺，空选返回 `400`，前端也会禁用保存。历史空选数据不会触发同步。
- 选店列表通过 `GET /api/order-sync/settings/shops` 读取已实现订单同步的平台下的全部授权店铺编号和名称，独立于店铺表格的分页，提供「全选可见店铺」和清空辅助。本地无店铺时返回空列表。实际同步订单时再经旺销王平台接口读取：店铺授权失效后该店会在定时轮次中记录失败，其他店铺继续，可在卡片中清空或重新选择。
- 定时批次并行同步选中的空闲店铺，单店失败会记录原因，其他店铺继续执行。可手动补跑失败的店铺，下一周期会重新扫描全部选中店铺。重复同步复用订单记录的幂等 upsert，不会写入重复订单。
- 手动与定时同步按店铺互斥。手动单店命令遇到该店已有任务时返回 `accepted: false`，手动全部同步与定时批次跳过已有任务的店铺。关闭定时同步只阻止后续周期，已受理的批次会继续执行完当前店铺列表。
- 不做跨进程或重启的作业持久化、断点恢复：任务状态仍在 API 内存中，重启后丢失；定时同步会在下一个周期重新执行选中店铺，已保存订单不受影响。

相关实现：[定时同步设置](../OpenERP.Api/Orders/OrderSyncSettings.cs)、[调度器](../OpenERP.Api/Orders/OrderSyncScheduler.cs)、[接口](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

## 本地诊断

在 Aspire Dashboard 的资源列表中查看状态、端点、控制台日志和追踪。终端诊断命令直接使用 PATH 中的 `aspire`。启动与停止见[本地开发](#启动与停止)。

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
- [Vite 开发代理](../OpenERP.Web/vite.config.ts) 优先将浏览器的 `/api` 请求转发到 Aspire 注入的 `API_HTTPS`，缺失时兼容手工配置的 `API_BASE_URL`。启动 Aspire 前按[环境准备](#环境准备)信任开发证书。单独启动 Vite 并连接私有 CA 或自签 HTTPS 上游时，可在启动 Node 前通过 `NODE_EXTRA_CA_CERTS` 指定受信的公开 PEM 证书。
- SeaweedFS 的 Master/Volume 健康检查之外，AppHost 另用签名 S3 `ListBuckets` 检查等待存储就绪，再启动 API 建桶。这是项目自定义的启动保护，不保证建桶必然成功；存储后续失去健康也不会自动暂停已运行的 API。
- 默认 AppHost 配置下，Development 环境的 `/health` 检查 PostgreSQL、Redis 和 SeaweedFS S3 连接。`/alive` 检查进程存活。

### 更新运行中的代码

- **后端 C# 代码**：在 Dashboard 资源列表找到 `api`，打开操作菜单，选择 **Rebuild（重新构建）**。等待资源恢复健康后，再回到业务页面刷新并重试。该操作重新构建并启动 API；只点 Restart（重启）不能当作已经重新编译的证据。
- **前端代码**：Vite 开发服务器通常会通过 HMR 自动更新页面。前端已更新不代表 API 已加载新代码；若出现新表单仍收到旧接口错误，先检查 `api` 的日志与重建状态。
- **自动热重载**：只有对应 IDE Hot Reload 或运行时 watch 已启用时，才依赖它应用后端修改；普通启动不代表自动启用了后端热重载。
- **AppHost 编排或配置**：需要应用新的资源配置时，在原仓库或 worktree 中重新执行相同的启动命令；需要第二个并行实例时才使用 `aspire start --isolated`（端口随机、机密隔离）。编辑器启动的实例由编辑器管理重启。

仅一个资源的实现发生变化时，优先使用该资源的 Rebuild 或已有热重载流程，不重启整个 AppHost。终端操作可先用 `aspire resource api --help --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive` 查看该资源可用的命令；列出 `rebuild` 时使用 `aspire resource api rebuild --non-interactive --apphost OpenERP.AppHost/OpenERP.AppHost.csproj`，随后 `aspire wait api --non-interactive --apphost OpenERP.AppHost/OpenERP.AppHost.csproj` 等待就绪。若没有重建命令，先确认资源类型和启动方式，不把 Restart 视为等价替代。

### 开发数据与密钥

在默认 AppHost 配置下，PostgreSQL、Redis 和 SeaweedFS 均使用持久容器与数据卷。PostgreSQL 挂载到 `/var/lib/postgresql`，Redis 和 SeaweedFS 挂载到 `/data`。卷名由 Aspire 根据应用身份与资源名生成，以当前环境为准；macOS Docker Desktop 的卷保存在其 Linux 虚拟机内，不在源码目录。

开发密码保存在 .NET User Secrets 中。只查看密钥文件位置、避免打印秘密值时使用：

```bash
aspire secret path --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

正常停止会保留持久资源；`aspire stop --force` 会删除持久资源，不能用于普通停止。保留数据卷时也应保留开发密钥及 `UserSecretsId`，避免密码不匹配。Redis 使用快照持久化，异常断电可能丢失最近的内存写入。

PostgreSQL `18-alpine` 和 Redis `8-alpine` 使用浮动大版本标签，小版本可能随镜像更新变化，本机缓存也会影响实际运行版本。SeaweedFS `4.53` 的普通发布镜像已基于 Alpine。[AppHost 镜像配置](../OpenERP.AppHost/AppHost.cs) · [SeaweedFS 发布构建](https://github.com/seaweedfs/seaweedfs/blob/4.53/.github/workflows/container_release_unified.yml) · [SeaweedFS Dockerfile](https://github.com/seaweedfs/seaweedfs/blob/4.53/docker/Dockerfile.go_build)

如果当前 checkout 已有 PostgreSQL `18` 创建的开发卷，首次切换到 Alpine 时请使用全新的默认 PostgreSQL 卷。例如，从新的 checkout 路径启动，Aspire 会为该路径创建默认卷。

## 使用 AI 开发工具

仓库代理规则见 [AGENTS.md](../AGENTS.md)。Codex、dsh 和 OpenCode 共用 `.agents/skills/` 中的 Aspire skills，由项目从[微软官方发布版](https://github.com/microsoft/aspire-skills/releases)同步。启动、停止或检查资源时，遵循项目内 [Aspire Skill](../.agents/skills/aspire/SKILL.md) 及其路由的子 Skill，直接调用 PATH 中的 `aspire` CLI。

用户说「帮我把项目跑起来」时，按[助手启动指南](../.agents/skills/openerp-startup/SKILL.md)判断运行方式、准备工具并验证页面。
