# 开发说明

贡献要求与协作规则见[贡献指南](../CONTRIBUTING.md)。

## 本地开发

### 环境准备

- 正式版 .NET **10** SDK，[global.json](../global.json) 选择已安装的最高 .NET 10 SDK。
- Node.js **26**，见 [.node-version](../.node-version)。
- pnpm **11.16.0**，见 [package.json](../OpenERP.Web/package.json)。
- PATH 中可直接调用的 Aspire CLI，版本与项目的 **17.0.0** 对齐；安装方式见 [Aspire 官方安装说明](https://aspire.dev/get-started/install-cli/)。
- 仅团队容器模式需要已启动的 Docker Desktop 或兼容引擎。

使用团队容器模式时，在仓库根目录安装依赖并信任开发证书。纯本机试用直接见[本机原生运行](#本机原生运行)。

```bash
dotnet tool restore
aspire certs trust
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

本文直接调用 PATH 中的 `aspire`。`dotnet tool restore` 还原仓库本地工具（包括 EF Core 工具），不替代 PATH 中 Aspire CLI 的安装。首次信任证书可能需要系统确认，已信任后无需重复操作。

### 本机原生运行

先准备上述源码构建工具。首次运行时用 `aspire certs trust` 信任本机开发证书，再从仓库根目录启动。Aspire 会准备 API 和前端的依赖。

```sh
aspire start --isolated --launch-profile native
```

打开 Web 资源的 HTTPS 地址。普通停止用 `aspire stop`，再次用相同的配置打开。原生方式用 SQLite 保存订单、账号及设置，用本机目录保存附件，用进程内缓存保存可重建的缓存内容。开发数据默认位于当前用户的本地应用数据目录下的 `OpenERP/workspaces/`，按源码工作区路径区分。移动或重新克隆源码前记录原数据位置；也可用环境变量 `OpenERP__DataDirectory` 指定绝对路径（例如另一个有空间的本地磁盘）。一个数据目录同一时间只供一个运行实例使用。

原生和容器模式各有自己的数据。备份原生实例时先正常停止应用，再完整复制数据目录，包括数据库和附件。在同一用户下还原时复制到空目录，并显式指定该路径。备份包含订单、附件及账户和 ERP 凭据，只存放在受控位置。正式 Docker 部署仍按[部署说明](deployment.md)操作。

#### 把改好的版本留在本机用

在一份新的输出目录发布当前源码。Windows x64 用以下命令，Linux x64 将 RID 换为 `linux-x64`。前端会和 API 一起构建。

```sh
pnpm --dir OpenERP.Web install --frozen-lockfile
dotnet publish OpenERP.Api -c Release -r win-x64 -p:PublishProfile=NativeLocal -o ./out/native-local
```

保留整个输出目录。在 Windows 运行其中的 `OpenERP.Api.exe`，在 Linux 运行 `OpenERP.Api`，用浏览器打开 `https://localhost:17083`。发布物自带 .NET 运行时和网页。默认复用当前用户的本机 HTTPS 开发证书；如这台电脑尚未完成本机证书信任，请由助手按前面的环境准备进行设置。需要自定义端口或证书时，使用 `Native__Port` 或 `Kestrel__Certificates__Default__Path`（程序目录之外的 PFX 绝对路径），有口令的证书用 `Kestrel__Certificates__Default__Password`。

日用数据默认在当前用户本地应用数据目录下的 `OpenERP/Local`。如需保留试用时的订单和附件，先正常停止开发实例，再把完整的原生数据目录复制到新的日用数据目录。也可用 `Native__DataDirectory` 明确指定日用目录。以后更新时先停程序并备份日用数据，运行新发布物时沿用这个目录和端口。只有需要让其他人通过网络访问时，才按[部署说明](deployment.md)配置团队站点。

### 启动与停止

在仓库根目录后台启动；Git worktree 或需要与其他本地实例分开运行时使用隔离模式：

```bash
aspire start --isolated
```

仓库的 `aspire.config.json` 已指定唯一 AppHost，无需重复指定项目。`--isolated` 隔离运行状态，不等于创建独立测试数据库或清空数据卷。普通后台启动可用 `aspire start`；需要终端前台运行时可用 `aspire run`。

也可以在安装了 Aspire 扩展的 VS Code 中，选择仓库提供的 `apphost` 启动配置运行；需要 worktree 隔离时使用上面的 CLI 入口。

打开启动输出中的 Aspire Dashboard，在资源列表查看健康状态。等 `api` 通过健康检查、`web` 就绪后，点击 `web` 的 HTTPS 地址打开应用。首次使用见[本地初始化与密码重置](#本地初始化与密码重置)。Dashboard 登录与应用管理员登录相互独立。

通过 `aspire start` 启动的后台实例，在同一仓库或 worktree 根目录运行 `aspire stop` 停止；关闭启动终端不会停止后台实例。终端前台 `aspire run` 按 `Ctrl+C` 停止；通过 VS Code 启动时使用编辑器的停止按钮。普通停止不要使用 `--force`，它会删除持久资源。

代码修改后见[更新运行中的代码](#更新运行中的代码)。数据卷与凭据的保留方式见[开发数据与密钥](#开发数据与密钥)。

## 代码导航

| 目录                                                   | 内容                              |
| ------------------------------------------------------ | --------------------------------- |
| [OpenERP.Web](../OpenERP.Web/)                         | Vue 前端与单元测试                |
| [OpenERP.Api](../OpenERP.Api/)                         | API、ERP 对接、订单同步与数据存储 |
| [OpenERP.AppHost](../OpenERP.AppHost/)                 | 本地服务编排                      |
| [OpenERP.ServiceDefaults](../OpenERP.ServiceDefaults/) | 健康检查、遥测与服务发现          |
| [OpenERP.Api.Tests](../OpenERP.Api.Tests/)             | 后端单元测试和集成测试            |

[router/index.ts](../OpenERP.Web/src/router/index.ts) 定义工作台、订单、商品量报表、授权店铺、用户管理与 ERP 接入路由，并为管理员保留仅内部比对用的视觉原型路由（`/visual-prototype`，不写入口与文档）。[views](../OpenERP.Web/src/views/) 存放页面，[components](../OpenERP.Web/src/components/) 提供可复用工作台区块、导航和控件，[workspace](../OpenERP.Web/src/workspace/) 承载工作台取数与状态推导，[ui](../OpenERP.Web/src/ui/) 提供 Tailwind 主题 token 与 `App*` 通用组件。前端无按页面拆分的全局样式目录，样式由工具类与 [theme.css](../OpenERP.Web/src/ui/theme.css) 统一承载，约定见[前端样式架构](#前端样式架构)。

## 构建与测试

工具版本以 [global.json](../global.json)、[.node-version](../.node-version)、[本地工具清单](../.config/dotnet-tools.json) 和 [package.json](../OpenERP.Web/package.json) 为准。

安装依赖后，在仓库根目录构建并运行测试：

```bash
dotnet build OpenERP.slnx --no-restore
dotnet test --solution OpenERP.slnx
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web format:check
pnpm --dir OpenERP.Web typecheck
pnpm --dir OpenERP.Web build
pnpm --dir OpenERP.Web test
```

API 的版本信息在构建时注入：`OPENERP_BUILD_VERSION`、`OPENERP_BUILD_COMMIT`、`OPENERP_BUILD_TIME` 三个环境变量由[发布工作流](../.github/workflows/release.yml)在构建 API 前导出，分别写入 `AssemblyInformationalVersion` 与程序集元数据；本地未设置时兜底 `0.1.0-dev`。只读接口 `GET /api/about` 匿名返回 `version`、`commit`（7 位）和 `buildTime`（UTC ISO 8601），前端缺失时显示 `dev`。

修改共享实体模型时，分别为 PostgreSQL 的 `AppDbContext` 和 SQLite 的 `SqliteAppDbContext` 生成迁移。在 `OpenERP.Api` 目录运行下面两条命令，将 `ChangeName` 换成本次变更名，并检查两份迁移。[EF Core 多提供程序迁移指南](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers)

```sh
dotnet ef migrations add ChangeName --context AppDbContext --output-dir Migrations
dotnet ef migrations add ChangeName --context SqliteAppDbContext --output-dir Migrations/Sqlite
```

### 后端集成测试资源与失败诊断

集成测试使用项目的 AppHost 模型，由 `AspireIntegrationFixture` 管理测试会话。fixture 初始化时启动 PostgreSQL 并等待 `openerp` 数据库健康。Aspire 17 同时准备 API 项目的构建产物。数据库与 CLI 测试可直接使用数据库，API 进程、Redis 和对象存储仍按需启动。需要存储的测试调用 `EnsureStorageStartedAsync`（获取存储连接时也会确保就绪）；HTTP 测试在异步初始化中调用 `EnsureApiStartedAsync`，并行准备存储和 Redis，待依赖就绪后启动 API。`StartWebAsync` 先准备 API，再启动前端安装器和前端。同步创建 HTTP 客户端不会隐式启动服务。

兼容的测试在同一 xUnit collection 内复用 PostgreSQL 服务器，数据库专用用例仍各自创建和删除独立数据库；共享 API 的用例保持串行。首次初始化与浏览器验收继续使用独立 fixture，避免共享账号、迁移和初始化状态。资源启动任务只执行一次，失败不会自动重试；已经启动的资源保留至 fixture 结束，因此全量运行中后续数据库用例可能与已启动的 API 共存。容器、密钥目录和存储卷属于本次测试会话，存储卷在测试内跨重启保留数据，最终随 fixture 清理。

认证并发测试先用真实 PostgreSQL 行锁确认操作进入等待队列，同时观察 CLI 或 HTTP 操作是否提前结束。默认每阶段锁等待最多 30 秒，真实子进程执行最多 90 秒；测试取消仍会提前终止等待。失败输出包含阶段、耗时、锁数量及操作结果，CLI 的退出码和已收集输出也会保留，子进程日志脱敏后交给 xUnit 的 `ITestOutputHelper`，随对应测试进入标准报告；不要在诊断中输出密码、stdin、私钥或完整连接字符串。失败后先结束测试持有的事务，再有界等待操作完成、取消并回收尚未结束的操作，最后释放连接和删除数据库；清理使用独立的 30 秒预算，事务释放与子进程回收各使用 10 秒子预算，主错误与清理错误同时报告。共享改密测试在释放观察连接后通过真实 CLI 恢复密码并撤销旧会话，避免晚到的改密请求再次改变共享账号。排障时先查看测试输出和 CTRF 报告，区分资源未就绪、操作提前退出、锁等待超时与清理失败，不以自动重跑掩盖失败。

### 前端静态检查

[ESLint 配置](../OpenERP.Web/eslint.config.js) 使用 flat config，以 `@eslint/js`、`typescript-eslint` 和 `eslint-plugin-vue` 的官方配置为基础。Vue 采用完整 `flat/recommended` 规则集，`eslint-config-prettier/flat` 放在最后关闭与 Prettier 冲突的排版规则；单元测试与浏览器测试分别使用 `@vitest/eslint-plugin` 和 `eslint-plugin-playwright` 的推荐规则。Playwright 按官方 `allowConditional` 选项仅允许现有真实会话环境条件下的 `skip`，不放宽无条件跳过。这套组合与仓库现有 Node.js 26、TypeScript 6、Vue 3.5 的版本范围兼容，且不引入旧式 `.eslintrc`。

[Prettier 配置](../OpenERP.Web/.prettierrc.json) 采用 create-vue v3.24.0 预设：两格缩进由 `.editorconfig` 提供，不加分号、使用单引号、行宽 100。格式检查覆盖 `src`、`tests`、`scripts`、前端根目录 JS/TS 配置、`index.html`、`package.json`、`tsconfig.json` 和 `.prettierrc.json`；依赖、构建产物、测试报告与生成的锁文件不在检查范围内。

ESLint 检查范围为 `src/**/*.{ts,vue}`、`tests/**/*.ts`、`scripts/**/*.mjs`、前端根目录的构建配置和 ESLint 配置本身；`node_modules`、`dist`、`coverage`、`playwright-report` 与 `test-results` 不参与检查。`src` 使用浏览器全局变量，构建配置使用 Node.js 全局变量，单元测试同时启用浏览器、Node.js 与 Vitest 环境，端到端测试使用 Playwright 规则并同时启用浏览器与 Node.js 全局变量（浏览器全局变量用于 `page.evaluate` 等页面内回调）。

```bash
pnpm --dir OpenERP.Web lint
pnpm --dir OpenERP.Web lint:fix
pnpm --dir OpenERP.Web format:check
pnpm --dir OpenERP.Web format
```

`lint` 和 `format:check` 只读检查，不修改文件；`lint:fix` 和 `format` 分别自动修复 ESLint 支持的问题与 Prettier 排版。两个 ESLint 入口都使用 `--max-warnings 0`，发现问题时返回非零退出码；`lint` 与 `format:check` 均接入本地和前端 CI。

类型检查由 `pnpm --dir OpenERP.Web typecheck`（`vue-tsc --noEmit`）执行。遇到 lint 报错或警告，按对应规则的官方文档修正成因，不用行内禁用或放宽配置绕过检查。

### 前端样式架构

前端样式使用 Tailwind CSS v4（[Vite 插件](../OpenERP.Web/vite.config.ts) 中的 `@tailwindcss/vite`）与 [src/ui](../OpenERP.Web/src/ui/) 下的 `App*` 组件，不再维护按页面拆分的全局 CSS 目录。[theme.css](../OpenERP.Web/src/ui/theme.css) 是唯一的样式入口：它按固定层序导入 Tailwind，关闭 preflight，并在 `@layer base` 提供全站 reset（盒子模型、按钮/链接/列表/标题重置、正文排版、滚动条、图标基础样式），在 `@theme static` 提供全部设计 token。

- 设计值只有一处来源：颜色、字号、行高、圆角、阴影都定义在 `@theme static` 的 token 中，命名空间决定工具类前缀（`--color-*` → `text-*`/`bg-*`/`border-*`/`stroke-*`，`--text-*` → `text-12/22` 等）。新增视觉值先按实测值补 token，禁止在模板里散落十六进制色值或自造近似值。
- `--spacing: 1px`，因此 `h-54`、`px-16`、`gap-12` 直接等于像素值；字号工具类的斜杠后为行高（`text-14/22`）。
- 优先复用 [src/ui](../OpenERP.Web/src/ui/) 的组件及其 props 表达上下文差异（尺寸档、色调、变体），页面和卡片只组合工具类，不复制组件内部结构。
- 类名必须是可静态扫描的字面量：不要在模板里拼接类名，按状态用互斥分支选择字面量。同一元素避免出现同属性冲突的工具类（如 `h-32` 与 `h-40` 并存），把公共部分与状态差异拆开。
- 不使用 `!important`。Tailwind 的 `max-[…]:` 变体是严格小于，需要“小于等于”语义时使用 `[@media(max-width:…)]:` 任意变体。
- `<style scoped>` 仅用于工具类无法干净表达的场景：伪元素、滚动条、粘性表格、复杂组合选择器、图表内部、原生 `<dialog>` 定位、几何形状（如工作台引导卡的箭头切角 `clip-path`）。颜色必须引用 token，尺寸同样优先使用 token 或在注释中说明实测来源；其余样式一律走工具类。
- 需要影响 `body` 等全局节点的布局例外（如登录页解除桌面最小宽度、报表页窄屏收缩）用 SFC 内的 `:global(...)` 保持未分层（unlayered），以便压过 `@layer base` 的 reset。
- 测试钩子使用 `data-testid`、role 与 ARIA，不依赖视觉类名；样式迁移时同步迁移选择器，不弱化断言。断言「视觉隐藏但保留无障碍名称」时，jsdom 单测只能检查 `sr-only` 约定，浏览器用例应改为断言实际裁剪尺寸（≤1px）等可观察结果。

### 前端视觉回归（视觉探针）

样式重构或布局调整后，用视觉探针核对渲染结果。探针按固定场景（视口、路由、API mock、交互步骤）截图并采集控件/排版/布局指标，产物写入 `OpenERP.Web/artifacts`（已忽略提交）。

```bash
# 采集当前代码到指定目录（会自动构建并启动预览服务）
VISUAL_PROBE=1 VISUAL_OUT=artifacts/issue93/after pnpm --dir OpenERP.Web visual:capture

# 对比两批产物：控件、调色板、布局指标按默认 4px 容差比较
pnpm --dir OpenERP.Web visual:compare artifacts/issue93/before artifacts/issue93/after
```

需要逐像素复核时，可用图像工具直接比较两批产物 `screenshots/` 下的同名 PNG。探针只覆盖桌面固定布局；移动端/窄屏行为以 Playwright 用例的视口断言为准。

### 测试环境

后端使用 xUnit v3、原生 .NET 10 Microsoft.Testing.Platform 和 Aspire.Hosting.Testing，配置见 [global.json](../global.json) 与[测试项目](../OpenERP.Api.Tests/OpenERP.Api.Tests.csproj)。订单 PostgreSQL 与文件存储测试共用隔离 Aspire 测试宿主，需要容器环境和受信任的开发证书。宿主自行启动和清理资源，订单用例另建并清理独立数据库，无需 `OPENERP_TEST_POSTGRES`。回归用例见 [OpenERP.Api.Tests](../OpenERP.Api.Tests/)。

前端单元测试位于 [tests/unit](../OpenERP.Web/tests/unit/)，使用 Vitest、Vue Test Utils 和 jsdom。Vite 代理回归测试在环回地址启动临时服务，需要 PATH 中有 `openssl` 生成临时自签证书。依赖和执行脚本以 [package.json](../OpenERP.Web/package.json) 为准。

[WebInstallerTests](../OpenERP.Api.Tests/WebInstallerTests.cs) 使用实际 AppHost 安装参数，在临时目录执行 pnpm 验证无 TTY 的依赖重建与冻结锁文件行为。需要 PATH 中有 Node.js 和 pnpm；fixture 只使用本地依赖，不连接 ERP，也不修改开发目录的 `node_modules`。

浏览器测试入口由 [Playwright 配置](../OpenERP.Web/playwright.config.ts) 指定。未指定外部地址时使用 `127.0.0.1:4173`。本地可复用该地址已有的服务，否则由 `webServer` 构建并启动预览服务；安装浏览器后直接运行：

```bash
pnpm --dir OpenERP.Web exec playwright install chromium
pnpm --dir OpenERP.Web test:e2e
```

未设置 `OPENERP_TEST_ADMIN_PASSWORD` 时，工作台用例通过页面级 mock 会话执行，账户与会话用例（[authentication.spec.ts](../OpenERP.Web/tests/e2e/authentication.spec.ts)）跳过；手动派发[前端工作流](../.github/workflows/ci-web.yml)时运行这类无需后端的浏览器用例，PR 不自动运行浏览器测试。设置 `PLAYWRIGHT_BASE_URL` 指向真实宿主时复用外部服务，不再启动 `webServer`。

账户端到端用例需要真实后端与 `OPENERP_TEST_ADMIN_PASSWORD`，会修改测试账户资料和密码。请使用隔离测试宿主，避免对日常开发账户运行这些用例：

```bash
OPENERP_RUN_BROWSER_TESTS=1 dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --filter-class '*BrowserAcceptanceTests'
```

该入口创建测试专用数据库，从未初始化状态启动 HTTPS 前端，将实际端点及本次测试要设置的密码通过环境传给 Playwright，并串行运行浏览器用例；测试密码不会注入 API 作为预置密码。员工角色用例覆盖零店铺代办授权、管理员同步目录和分配、负责人核对历史小计与手动同步，以及未分配账号被拒绝读取。该宿主仅替换外部 ERP 的 HTTP 传输。初始化和角色用例保存实际界面截图，完成后由 Aspire 测试夹具清理测试资源。它先通过 .NET 严格校验前端证书，再通过 `NODE_EXTRA_CA_CERTS` 将已校验的服务端 PEM 证书提供给 Node 客户端，不跳过证书校验；临时文件在结束后删除。独立运行 Playwright 时，Node 如不识别本机开发证书，需用 `NODE_EXTRA_CA_CERTS` 指定对应的公开 PEM 证书。不设置开关时，此浏览器入口明确跳过，不计作浏览器验收通过。

### 真实 ERP 验证

常规后端测试自行准备隔离 PostgreSQL，不需要连接开发数据库。真实 ERP 验收通过进程环境变量启用：

| 变量                     | 用途与前提                                                                                                                                                                                       |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `OPENERP_TEST_ERP_TOKEN` | 启用 SID `644928` 的[只读分页验证](../OpenERP.Api.Tests/OrderLiveAcceptanceTests.cs)（需超过 100 单），以及 SID `30` 的[报表核对](order-snapshot-report.md#验证)（只读源端，写入测试专用数据库） |
| `OPENERP_TEST_POSTGRES`  | 与上述 Token 一起启用 SID `30` 的源端与入库结果比对；指向已完成该店手动同步的数据库                                                                                                              |

使用 `OPENERP_TEST_POSTGRES` 的比对用例的前置步骤是在浏览器完成 SID `30` 的两次手动同步；测试本身只读源端和数据库，不执行同步或建表。未设置相应变量时用例跳过。凭证仅通过进程环境提供，不写入文档或版本库；测试命令见本页[构建与测试](#构建与测试)。

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

唯一管理员 `admin` 管理用户、系统接入、全局目录与定时同步，并能读取所有店铺。员工使用 `User` 角色，可以从店铺列表去旺销网授权新店。员工的目录、平台选项、订单、报表、工作台和手动同步范围由服务端读取当前分配。

`UserShopAssignments` 以用户 UUID、平台和 SID 为复合键。迁移为存量员工建立空范围。管理员在用户管理中选择已同步到本地目录的店铺，可以保留或撤销暂时下架的已有分配。归属独立于目录行，因此历史订单继续可查，同平台同 SID 恢复后沿用原分配。删除员工会在同一事务中清理归属和会话。

管理员用户名固定。姓名必填（最多 20 个字符），手机号可选（最多 20 个字符），邮箱可选并校验格式；手机号、邮箱仅作为个人资料。菜单中的资料与最近登录时间来自数据库，保存资料不会改变最近登录时间。

### 本地初始化与密码重置

Development 环境启动 API 时自动应用 EF Core 迁移。对于已创建的数据库，启动代码先通过 EF 准备迁移历史表，避免 Npgsql 10.0.3 在空库首次启动时查询缺失的表（[上游问题 #3407](https://github.com/npgsql/efcore.pg/issues/3407)）。API 每次启动生成临时 RS256 签名密钥。空库首次启动后，在应用的 HTTPS 页面设置管理员密码，再使用 `admin` 登录。已有登录的浏览器可以在 API 重启后使用持久刷新 Cookie 静默恢复。正式环境的配置、初始化和升级见[部署说明](deployment.md)。

启动时只在管理员行缺失时创建占位行。设置密码通过数据库条件更新完成，并发请求只能有一方成功。初始化成功记录 UTC 时间、连接来源 IP 和 User-Agent，不记录密码，也不签发令牌或更新最近登录时间。

本地忘记密码时，构建后从仓库根目录运行下列命令。重置进程通过 `ConnectionStrings__openerp` 连接开发数据库，需要单独提供连接配置。

```bash
dotnet OpenERP.Api/bin/Debug/net10.0/OpenERP.Api.dll reset-admin-password
```

命令以隐藏输入提示新密码和确认密码，并撤销管理员在所有设备上的登录。正式环境的操作与自动化方式见[管理员密码重置](deployment.md#重置管理员密码)。

### 会话规则与接口

API 使用 RS256 签发 Bearer access token，`sub` 为用户 UUID，`role` 为 `Administrator` 或 `User`。令牌最长有效期为 20 分钟，且不超过刷新期限。验证时不设时钟宽限。前端只在内存中保存 access token，每 5 分钟主动尝试刷新。刷新期限自登录起固定为 24 小时，刷新不延长期限或轮换凭证。刷新值只通过 `HttpOnly`、`Secure`、`SameSite=Strict` 的持久 Cookie 传递，Cookie 仅发往当前主机的 `/api/auth` 路径。服务端只在 PostgreSQL 的独立 `RefreshTokens` 表中保存刷新值的哈希。浏览器关闭或 API 重启后仍可使用有效的刷新凭证。

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

前端通过应用同源的 HTTPS `/api` 地址访问后端。初始化、登录、刷新和退出要求 `X-CSRF-TOKEN` 请求头及对应 CSRF Cookie，先请求 `/api/auth/csrf` 取得令牌。防伪校验继续使用存放于 PostgreSQL 的 Data Protection 密钥。Bearer 认证的业务写请求，包括密码修改、资料保存和文件上传，无需附带 CSRF 令牌。业务 API（包括 `/api/orders`、ERP 与文件）默认需要登录，`/api/about` 与健康检查保持匿名。

`account`、`GET /api/auth/me` 和资料保存响应包含 `id`、`role`、`username`、`name`、`phone`、`email`、`lastLoginAt`、`expiresAt`，时间使用 ISO 8601。这里的 `expiresAt` 表示 24 小时刷新期限；access token 的到期时间单独由 `accessTokenExpiresAt` 返回。未初始化时登录及受保护接口返回 `409`，ProblemDetails 顶层 `code` 为 `initialization_required`；初始化重复请求或并发败者返回 `409`、`already_initialized`。前端保留安全的原目标地址，完成初始化和登录后回到有权访问的页面。普通用户访问用户管理、ERP 配置或定时同步设置接口返回 `403`。

普通用户改名后，旧用户名立即可用。删除时保留原行与 UUID 并撤销该账号的登录，满 7 天后可重新分配该用户名。新建同名账号获得独立 UUID。

初始化、退出和改密成功返回 `204`。错误使用 ProblemDetails：输入或旧密码错误为 `400`，凭证或登录标识失效为 `401`，数据库不可用或管理员记录异常缺失为 `503`。初始化请求断线或服务错误后只重新查询状态，不自动重放写请求；查询已初始化不代表刚才的请求一定是赢家，应转到登录页确认。

## ERP 接入与接口

上游地址固定为 `https://v3-api.wxwerp.com`。管理员在账户菜单「系统设置」打开的 ERP 接入页填写或更换 Token。API 使用 ASP.NET Core Data Protection 保护 Token，并保存在 PostgreSQL 中。备份数据库时一并保留 Data Protection 密钥表。页面只显示保存状态。验证凭证与店铺权限时，以实际 ERP 请求的结果为准。

| 接口 | 行为 |
| --- | --- |
| `GET /api/erp/order-sync-availability` | 已登录用户查询 `configured` 和 `orderSyncEnabled`，供业务页面判断能否同步 |
| `GET /api/erp/config` | 管理员查询配置记录是否存在，返回 `configured`、`baseUrl` 和 `orderSyncEnabled`。查询失败返回 `503` |
| `PUT /api/erp/config` | 管理员提交 `{ accessToken }` 保存或更换 Token，成功返回 `204`。下一次读取凭证的操作使用新值 |
| `GET /api/erp/shops?page=1&pageSize=20&platform=&status=&keyword=` | 按账号范围分页读取本地目录，每页 1–100 条。员工可查看所属店铺的状态、失败原因和检查时间。管理员额外获得 `summary`，可按 `status=valid/invalid/unknown` 筛选 |
| `GET /api/erp/platforms` | 返回当前账号可见的本地店铺涉及的平台（名称、展示名、图标、是否生产可用、店铺数）；平台元数据来自上游同步，不在本地写死 |
| `GET /api/erp/shop-sync` | 仅管理员可读取。返回店铺同步的当前或最近一次状态（运行中、开始/完成时间、总数、已检查、有效/无效/未知、错误、单调递增的 `version`） |
| `GET /api/erp/order-sync-settings` | 仅管理员在 Development / Production 可用；返回定时同步的 `enabled`、`shopSids` 和只读 `intervalMinutes` |
| `PUT /api/erp/order-sync-settings` | 仅管理员在 Development / Production 可用；保存启用状态和店铺选择；启用时至少选择一家店铺 |
| `GET /api/erp/order-sync-settings/shops` | 仅管理员在 Development / Production 可用。从本地目录返回全部 TemuPop 店铺。凭证缺失、凭证或本地目录读取失败返回 `503` |

每轮授权店铺同步、每家店的订单同步和每次定时同步选店请求，分别通过 scoped `IErpTokenStore.GetAsync` 读取并解密一次 Token。读取失败由存储层返回 `OpResult`。配置状态只检查记录是否存在，因此旧密文损坏时管理员仍可查看配置并替换凭证。

`IErpClient` 的每个上游方法都显式接收不可变的 `ErpAccessToken`。同一操作的分页、详情和并发状态查询共用该快照。`WxwErpClient` 只处理 HTTP 和上游响应，并发期间各请求无需访问凭证存储。参见 [EF Core 对 DbContext 并行使用的说明](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)。

本地店铺接口只读 PostgreSQL；数据库暂时不可用时返回 `503`，不会回源上游。旧 `/api/erp/authorization/*` 接口、授权快照与事件表已删除。实现见 [ShopDirectoryEndpoints](../OpenERP.Api/Erp/ShopDirectoryEndpoints.cs)、[ShopSyncJob](../OpenERP.Api/Erp/ShopSyncJob.cs)、[ErpHub](../OpenERP.Api/Erp/ErpHub.cs) 和 [OrderSyncEndpoints](../OpenERP.Api/Orders/OrderSyncEndpoints.cs)。

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
- 订单同步仍只处理 TemuPop（见下文订单同步边界）；店铺目录本身支持上游返回的全部平台。

### SignalR 同步

- 单 Hub：`/api/hubs/erp`。普通读取（订单列表、店铺目录、平台、配置、店铺同步状态）继续使用 HTTP；只有长时同步与进度使用 SignalR。
- 浏览器通过 `access_token` 查询参数连接 Hub。JWT 中间件只对 `/api/hubs` 路径读取该参数，其他路径忽略，避免 token 泄漏到普通接口。
- 管理员方法 `StartShopSync()` 返回 `accepted`、当前状态与错误；服务端事件 `ShopSyncUpdated(ShopSyncStatus)` 在管理员连接建立、同步开始、每 10 家店检查、完成或失败时向管理员推送。状态带单调递增 `version`，客户端据此丢弃迟到的 HTTP/invoke 旧快照。
- 订单列表通过 `StartOrderSync(sid)` 启动单店同步，返回 `accepted`、该店状态与错误。「全部同步」通过一次 `StartAllOrderSync()` 命令读取本地目录中当前账号可见的 TemuPop 店铺，覆盖所有分页和授权状态，返回本次受理和跳过的店铺数。
- 单店命令在读取状态或受理前按当前分配检查 TemuPop 和 SID。越权返回 `accepted: false` 和空状态。两种手动订单同步命令均在受理前检查 ERP 凭证，并为员工的当前连接订阅本次可同步店铺的进度。因此连接建立后新分配的店铺也能在原连接收到终态。受理后关闭面板或断开连接，任务继续执行。若凭证无法解密，该店任务在 `shop` 阶段报告失败。
- 连接或重连时，`OrderSyncSnapshot` 提供当前进程的 `epoch` 和可见店铺的最近状态。管理员接收全部进度，员工连接按当次分配加入各自的店铺组，重连时重建。手动与定时任务通过 `OrderSyncUpdated` 推送带版本的状态，包含阶段、进度、入库计数和跳过原因。成功或部分成功后，页面按当前筛选刷新本地订单。
- 客户端使用 `withAutomaticReconnect`；access token 由 `accessTokenFactory` 每次读取最新值，配合会话刷新处理 20 分钟 JWT 到期。连接断开时页面仍可浏览本地数据，但同步按钮不可用。
- Hub 映射开启 `CloseOnAuthenticationExpiration`：JWT 到期即关闭连接，客户端用刷新后的 access token 重连，避免连接无限期沿用旧凭证。
- `StartShopSync`、`StartOrderSync` 和 `StartAllOrderSync` 执行前按当前数据库会话校验一次登录状态；会话已撤销时拒绝命令并断开连接。这是官方文档给出的“在 Hub 方法内按当前数据校验”补偿，不是逐消息重新认证；普通 HTTP 接口仍按请求执行完整会话校验。
- 撤销分配后，新 HTTP 请求与同步命令使用数据库中的最新范围。已有连接的进度组成员资格在正常断线或 JWT 到期重连时更新。撤销会话后，新的受保护 HTTP 请求返回 `401`。
- 当前假设单个 API 实例；多实例部署需要 SignalR backplane，非本期范围。

## 本地订单列表

工作台的订单列表和[商品量报表](order-snapshot-report.md)都读取 PostgreSQL 中的订单快照。订单列表使用已认证的 `GET /api/orders`，支持 `shopSid`、`platform`、`number`、`status`、`createdFrom`、`createdTo` 和分页。它按主单计数和翻页，每页默认 20 条。订单号片段和状态同时指定时，两个条件须命中同一个平台订单；返回整张主单上的全部号码，并标出命中项。行数和件数均按整张主单计算。店铺名称来自本地目录，订单所属店铺已从目录移除时，仍可通过 SID 展示和筛选。

订单列表按来源创建日期（`YYYY-MM-DD`）筛选，含起止日，默认展示全部历史。wxw 可配置年月日的显示顺序，本地保留了时间原值。像 `08-09-2025` 这样有两种有效解释的日期按未知处理。有效的创建时间按倒序排列，缺少有效时间的订单排在末尾，指定日期范围时排除。创建和出库时间按来源的日期和精度显示。筛选及分页无需访问上游。

## 订单同步边界

- API 在 Development 启动时应用 EF Core 迁移。主单以 ERP 内部 ID 标识，平台 PO 和商品行关联主单；订单时间保留 ERP 显示原值及来源。
- 完整读取并校验列表和详情后才写库；读取失败时不保存本批结果，数据库保存失败时整批回滚。
- 归属异常的订单整单跳过，任务显示部分成功和跳过原因。只刷新本批通过校验的订单，未返回或被跳过的已有订单会保留；已接受订单的子项按本次结果更新。
- 同步任务状态保存在 API 内存中，每店保留最近一次任务。手动和定时同步共用 4 个店铺执行位置，同一店铺同时只接受一个任务。其余已受理店铺在面板显示「等待同步」，获得空位后读取 ERP Token。各店分别保存，写入时串行更新以 ERP 内部 ID 标识的主单。刷新网页可恢复状态，API 重启后状态丢失；手动同步需重新触发，定时同步会在下一周期重跑，已保存的订单仍在数据库中。
- 订单列表每页读取 500 条，按 ERP ID 排序，并核对前后计数、每页条数及 ID 唯一性。上游使用偏移分页，没有一致性快照：扫描期间若不同订单同时进入和退出可见范围，即使总数不变仍可能漏读。两种页长得到相同结果仅能证明当次扫描一致。
- wxw 商品详情接口一次接收一个 ERP 订单 ID。所有店铺在一个 API 进程内共享 8 个详情读取位置，单店可以使用全部空位。每单按需补读时间，按原列表顺序汇总进度。单店完整读取并校验后，EF Core 批量写入并一次提交。
- 订单保存通过 `IOrderChangeCache` 复用已落库订单的 SHA-256 指纹。其 `HybridCache` 实现在两种模式都使用进程内缓存，容器模式还使用 Redis 作为二级缓存。每次先从数据库批量读取主单 ID 和版本。当前版本与完整订单内容都命中时，直接计为「未变」。新单和未命中的订单走正常保存或明细比对。所有订单写入入口都须在业务数据变化时于同一事务内更换主单版本，包含仅子单或商品行变化的情况。缓存于保存成功后填充。缓存过期或丢失可从数据库重新比对，订单详情仍逐单实时读取。

相关实现：[订单读取器](../OpenERP.Api/Erp/WxwErpClient.Orders.cs)、[任务状态](../OpenERP.Api/Orders/OrderSyncJobs.cs)、[订单保存](../OpenERP.Api/Orders/OrderStore.cs)。

## 定时同步边界

- 定时同步与手动同步同样只在 Development / Production 提供。订单列表工具栏「同步历史订单」面板中的「定时同步」卡片只配置是否启用和选择店铺，不提供周期配置。
- 周期来自 API 配置 `OrderSync:SchedulerIntervalMinutes`（默认 `30`，允许 `1`–`1440` 分钟），启动时校验；修改后需重启 API，保存设置本身不需要重启。每轮受理后等待一个周期再尝试下一轮；若上一轮仍在运行，本轮跳过并等下一周期。启用后首次执行同样等待一个周期。
- 设置保存在 PostgreSQL 的 `OrderSyncSettings` 单行表，使用 UUIDv7 主键和独立的数据库单行约束。保存后 API 唤醒调度器重新读取设置；启用后首次执行仍等待一个完整周期。
- 启用时必须至少选择一家店铺，空选返回 `400`，前端也会禁用保存。历史空选数据不会触发同步。启用还要求已配置私有 ERP AccessToken；未配置时保存启用状态返回 `503`，前端禁用开关和保存。
- 选店列表通过 `GET /api/erp/order-sync-settings/shops` 读取最近成功落库的全部 TemuPop 店铺 SID 和名称，独立于本地店铺表格的分页，提供「全选可见店铺」和清空辅助。wxw 离线期间仍可选店和保存设置；本地无店铺时返回空列表。实际同步订单时再向 ERP 校验店铺可见性：Token 权限变化后该店会在定时轮次中记录失败，其他店铺继续，可在卡片中清空或重新选择。
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
- 容器方式的 Development 环境通过 `/health` 检查 PostgreSQL、Redis 和 SeaweedFS S3 连接。`/alive` 只检查进程存活。

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

仓库代理规则见 [AGENTS.md](../AGENTS.md)。Codex、dsh 和 OpenCode 共用 `.agents/skills/` 中的 Aspire skills，由项目从[微软官方发布版](https://github.com/microsoft/aspire-skills/releases)同步。启动、停止或检查资源时，遵循项目内 [Aspire Skill](../.agents/skills/aspire/SKILL.md) 及其路由的子 Skill，直接调用 PATH 中的 `aspire` CLI。
