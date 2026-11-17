# 代理工作约定

## 文档分工

- `README.md` 面向人类，保留项目用途、现有能力和上手路径，不写阶段性计划、联调流水账或代理指令。
- [CONTRIBUTING.md](CONTRIBUTING.md) 面向人类贡献者，集中维护贡献原则、测试要求和提交约定；`docs/` 保存面向人类的开发操作与技术说明。代理直接引用这些共享知识，不另建重复副本。
- 本文件只记录长期适用、跨任务复用的原则和工作约定，不记录功能进度、临时实现状态、测试清单或历史验收结果。纠错时提炼可复用的规则，不把当次状态写成长期指令。
- 可复用的代理专项流程放在 `.agents/skills/`。阶段计划、执行状态与验收记录放在对应任务、Issue 或 PR；修改行为时同步核对相关文档。

## 协作与执行约定

- 开始工作前阅读并遵守[贡献指南](CONTRIBUTING.md)中的开发约定、编码风格、BDD/TDD、配置保护与提交要求；这些要求对代理同样有效。
- 全程使用中文沟通。准确遵循用户要求；存在实质性理解分歧或范围、兼容性取舍时先确认，其余情况在授权范围内自主继续。
- 安装与人工启动入口见 [README](README.md#快速开始)；代理的 Aspire 生命周期操作必须按下述 Skills 执行，不能直接套用人工启动流程。
- 代理执行 .NET `restore`、`build` 必须直接使用沙箱外权限；`run`、`test` 涉及隐式或显式还原时同样适用。
- 遵守[贡献指南](CONTRIBUTING.md#测试与验证要求)的验证要求，按[开发说明](docs/development.md#构建与测试)执行验证，并在任务回复中汇报实际验证结果和未验证项；纯文档改动按内容验证，不编造红绿测试。

## PR 提交与评审约定

- PR 合并必须由人类开发者执行，代理无权合并。
- PR 合并前必须确认 PR head 的 status checks 全部通过；检查失败、未完成或状态未知时不得合并。
- 提交信息遵循 Conventional Commits。
- PR 必须附验收截图，临时验收产物禁止提交进 git。
- Review 线程只有在问题真正修复并验证后才回复并 resolve。
- CI/测试类改动需先完成本地验收（含失败归因验证），再提 PR。

## Aspire 官方指导（强制）

- 涉及 Aspire 的开发、配置、运行或排障前，必须读取并严格遵循项目内 [Aspire Skill](.agents/skills/aspire/SKILL.md)，再按其路由读取对应子 Skill；不得仅凭经验或本文件摘要执行。项目内 Skill 优先于同名插件 Skill。
- 以项目实际 Aspire 版本、官方文档和 API 参考为准。编辑 AppHost 代码前，必须执行 `aspire docs api search <query> --language csharp`；涉及不熟悉的 API，先执行 `aspire docs search <topic>`。不得猜测 API、参数或使用已废弃的做法；无法取得依据时，明确报告缺口，不以猜测继续相关改动。
- AppHost 生命周期严格遵循 `aspire-orchestration`：明确唯一目标，按其规则选用编辑器生命周期工具或 Aspire CLI。Git worktree 中使用带 `--isolated`、`--apphost <path>` 和 `--non-interactive` 的 `aspire start`；不得用直接 `dotnet run`、手工 Docker 或进程操作替代 Aspire 编排。
- 与资源交互前必须使用 `aspire wait <resource>` 等待就绪，不得以手工 HTTP 轮询替代。通过 `aspire describe` 获取资源状态和实际端点，不硬编码动态端口。运行异常先按 `aspire-monitoring` 检查状态、日志和追踪，再决定代码改动。
- 资源代码变化优先使用资源命令、运行时 watch/HMR 或 Skill 允许的调试流程；仅因一个资源变化不得重启整个 AppHost。禁止用删除 `bin/`、`obj/` 或强杀进程解决运行锁定；按 Skill 的停止和恢复流程处理。`aspire stop --force` 会删除持久资源，必须先取得针对明确 AppHost 及数据丢失的用户授权。
- 直接调用 PATH 中的 `aspire` CLI，不额外套用 `dotnet tool run`；代理启动及其他生命周期操作严格遵循项目内 Aspire Skill。普通 .NET 构建和测试按[开发说明](docs/development.md#构建与测试)执行。用户明确指令优先于 Skill；若 Skill 导致请求确认、暂停或无法完成任务，必须链接实际读取的 Skill、引用相关原文并说明适用理由，不得自行扩大任务范围。
- 保持官方代理集成方式，不因人工启动入口不同而改写 MCP 配置；客户端配置说明见[开发说明](docs/development.md#使用-ai-开发工具)。
