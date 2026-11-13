# 仓库贡献指南

## 语言与协作要求

- 全程使用中文沟通；文档和代码注释必须使用中文。命令、路径、标识符及专有名称保留原样。
- 准确遵循用户要求，保持改动最小；涉及实质性范围或兼容性取舍时，先确认再实施。

## Aspire 官方指导（强制）

- 涉及 Aspire 的开发、配置、运行或排障前，必须读取并严格遵循项目内 [Aspire Skill](.agents/skills/aspire/SKILL.md)，再按其路由读取对应子 Skill；不得仅凭经验或本文件摘要执行。项目内 Skill 优先于同名插件 Skill。
- 以项目实际 Aspire 版本、官方文档和 API 参考为准。编辑 AppHost 代码前，必须执行 `aspire docs api search <query> --language csharp`；涉及不熟悉的 API，先执行 `aspire docs search <topic>`。不得猜测 API、参数或使用已废弃的做法；无法取得依据时，明确报告缺口，不以猜测继续相关改动。
- AppHost 生命周期严格遵循 `aspire-orchestration`：明确唯一目标，按其规则选用编辑器生命周期工具或 Aspire CLI。Git worktree 中使用带 `--isolated`、`--apphost <path>` 和 `--non-interactive` 的 `aspire start`；不得用直接 `dotnet run`、手工 Docker 或进程操作替代 Aspire 编排。
- 与资源交互前必须使用 `aspire wait <resource>` 等待就绪，不得以手工 HTTP 轮询替代。通过 `aspire describe` 获取资源状态和实际端点，不硬编码动态端口。运行异常先按 `aspire-monitoring` 检查状态、日志和追踪，再决定代码改动。
- 资源代码变化优先使用资源命令、运行时 watch/HMR 或 Skill 允许的调试流程；仅因一个资源变化不得重启整个 AppHost。禁止用删除 `bin/`、`obj/` 或强杀进程解决运行锁定；按 Skill 的停止和恢复流程处理。`aspire stop --force` 会删除持久资源，必须先取得针对明确 AppHost 及数据丢失的用户授权。
- 本仓库通过 `dotnet tool run aspire` 调用锁定的本地 CLI；上文 `aspire ...` 均指该入口。普通 .NET 构建和测试仍按下文执行。用户明确指令优先于 Skill；若 Skill 导致请求确认、暂停或无法完成任务，必须链接实际读取的 Skill、引用相关原文并说明适用理由，不得自行扩大任务范围。

## 项目结构

当前仓库以开发骨架为主，已接入 Temu 半托管跨境店铺的只读查询，以及仅在 Development 环境开放的 SeaweedFS 文件存储；尚未实现本地订单保存。

- `OpenERP.AppHost/`：通过 Aspire 编排前端、API、PostgreSQL、Redis 和 SeaweedFS。
- `OpenERP.Api/`：ASP.NET Core Minimal API、EF Core `AppDbContext`、私有 ERP 店铺查询及开发环境文件存储 API。
- `OpenERP.ServiceDefaults/`：共享健康检查、遥测和服务发现配置。
- `OpenERP.Web/src/`：Vue/TypeScript 前端；`router/` 定义 `/` 首页和 `/shops` 店铺页路由，`views/` 存放页面，`styles.css` 提供共享样式；尚无独立静态资源目录。
- `OpenERP.slnx`：.NET 解决方案；详细启动和诊断步骤见 `README.md`。

## 构建与本地开发

使用 .NET SDK 10.0.404、Node.js 26.13.0、pnpm 11.16.0，并启动兼容 Docker 的容器运行环境。在仓库根目录执行：

```bash
dotnet tool restore                         # 安装本地 Aspire CLI
dotnet tool run aspire certs trust --non-interactive # 信任开发证书
dotnet restore OpenERP.slnx --locked-mode    # 按锁文件还原依赖
pnpm --dir OpenERP.Web install --frozen-lockfile # 安装前端依赖
dotnet build OpenERP.slnx --no-restore       # 构建 .NET 项目
pnpm --dir OpenERP.Web typecheck            # 检查 Vue/TypeScript 类型
pnpm --dir OpenERP.Web build                # 构建前端资源
dotnet tool run aspire start --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive --isolated
dotnet tool run aspire wait web --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
dotnet tool run aspire describe --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

上面的启动示例适用于 Git worktree；普通检出按 `aspire-orchestration` 选择启动方式。Aspire 自动启动前端。端口动态分配，请通过 Dashboard 打开 `web` 地址。停止必须遵循该 Skill 的生命周期工具及结果处理规则；允许 CLI 停止时，执行 `dotnet tool run aspire stop --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive`。

代理执行 .NET `restore`、`build` 必须直接使用沙箱外权限；`run`、`test` 涉及隐式或显式还原时同样适用。

## 编码风格与命名

遵循 `.editorconfig`：使用空格，C# 缩进四格，JSON/XML 缩进两格；采用文件范围命名空间，类型和方法使用 PascalCase，接口以 `I` 开头，私有实例字段使用 `_camelCase`。

C#/.NET 后端处理可预期失败时优先使用 Result 模式；适用时优先采用 `RokyZevon.OpResult`，避免用异常控制正常流程。

前端沿用两格缩进、单引号、不加分号、PascalCase Vue 文件名及 `<script setup lang="ts">`。TypeScript 已开启严格检查，尚未配置 ESLint 或 Prettier。

## 测试与验证

默认在仓库根目录执行全量测试。

```bash
dotnet test --solution OpenERP.slnx
pnpm --dir OpenERP.Web test
pnpm --dir OpenERP.Web test:e2e
```

后端使用 xUnit v3、原生 .NET 10 Microsoft.Testing.Platform 和 Aspire.Hosting.Testing。文件存储集成测试需要运行中的 Docker 兼容容器环境和受信任的 HTTPS 开发证书，测试宿主自行启动和清理资源。后端还包含重启就绪顺序、独立 HTTP 上传校验和 S3 健康状态模型回归。

前端单元测试位于 `OpenERP.Web/tests/unit`，使用 Vitest、Vue Test Utils 和 jsdom。浏览器测试位于 `OpenERP.Web/tests/e2e`，使用 TypeScript 和 Playwright Test。浏览器测试环境需安装 Chromium（`pnpm --dir OpenERP.Web exec playwright install chromium`），目标地址由 `PLAYWRIGHT_BASE_URL` 提供。

在任务回复中汇报实际验证结果和未验证项。

### BDD/TDD 开发流程（强制）

- **先行为，后实现（BDD）**：新增或修改行为前，先依据需求写出中文 `Given / When / Then` 验收场景，明确前置条件、触发动作、可观察结果，以及相关失败、边界场景和必须保持的既有行为。存在实质性歧义时先确认；需求明确时自主推进，不为形式重复请求批准。
- **红灯（Red）**：先将当前场景落实为可执行测试并运行，确认测试因目标行为尚未实现或既有缺陷而失败，再编写对应实现。修复缺陷必须先有能复现缺陷的回归测试；编译错误、依赖缺失或环境故障不能冒充有效红灯。禁止先写实现再补测试，并将其称为 TDD。
- **绿灯（Green）**：仅编写使当前失败测试通过所需的最少实现，运行测试确认通过，再进入下一个场景。不得跳过、删除或弱化断言来获得绿灯；需求变化时先同步行为场景和测试依据。
- **重构（Refactor）**：绿灯后再做必要重构，保持行为不变并重新运行相关测试。纯重构须先确认相关行为已有测试保护且基线通过；保护不足时先补测试，再重构。
- **按行为选择验证层次**：业务规则使用单元测试，跨组件或数据交互使用集成测试，用户关键流程按需使用浏览器测试。断言必须验证可观察结果，不能只验证 mock 调用或复制实现逻辑。沿用现有测试栈，不因 BDD 强制引入新框架；涉及 Aspire 的集成与端到端验证仍须遵守上述官方指导。
- **以证据交付**：完成前运行相关回归测试和必要构建、类型检查，记录行为场景、有效红灯、绿灯及最终回归的命令和结果。测试无法执行时明确记录阻塞和未验证项，不得宣称已通过 BDD/TDD 验收。纯文档等不改变可执行行为的改动按内容验证，不编造红绿测试；其他无法遵循上述流程的情况须先说明原因并取得用户明确同意，不得自行豁免。

## 提交与合并请求

现有提交采用 `feat: ...`、`chore(aspire): ...` 等 Conventional Commits 格式。提交应聚焦单一目的。合并请求说明应包含行为变化、关联工单或问题、验证结果；界面变更附截图。

## 配置与数据保护

密钥保存在 .NET User Secrets，禁止写入 Git。保留依赖锁文件和持久开发数据卷；不要随意重置 UserSecretsId、删除开发密钥或数据卷。
