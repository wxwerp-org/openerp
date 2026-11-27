# 代理工作约定

开始工作前阅读并遵守[贡献指南](CONTRIBUTING.md)。使用框架、库或工具前，先查项目所用版本的官方指导，优先采用框架约定和内置能力。查不到依据时说明缺口，不猜测 API 或参数。

## 执行约定

- 遇到警告（含 ESLint），按该诊断的官方指导修正成因，不得忽略、屏蔽或降级。
- .NET `restore`、`build` 直接在沙箱外执行。`run`、`test` 涉及显式或隐式还原时同样适用。
- 涉及 Aspire 时，先读并遵循[项目内 Aspire Skill](.agents/skills/aspire/SKILL.md)，按路由名称读取仓库内对应的子 Skill。直接调用 PATH 中的 `aspire`。
- 用户让编码助手在本机启动、验证页面或把版本留在本机时，遵循[助手启动指南](.agents/skills/openerp-startup/SKILL.md)，按其中判断依据和示例措辞行动。
- 用户明确指令优先于 Skill。若 Skill 导致请求确认、暂停、未完成或偏离任务，链接实际读取的 Skill，引用原文并区分其要求与自己的判断。
- 前端基于 SpiderUI（Art Design Pro）框架：Vue 3、Element Plus、Pinia、vue-i18n，约定见[前端结构与约定](docs/development.md#前端结构与约定)。新增或修改界面时优先复用 Element Plus 组件和 `src/components/core` 里的通用组件（`ArtTable`、`ArtTableHeader` 等），主题和颜色通过主题变量与 `src/assets/styles` 调整，不在页面里硬编码。后端调用统一走 `src/utils/http`，不要在页面里直接 `fetch`。

## 运行与调试

- 改完代码后直接把完整系统运行起来，让用户刷新页面即可调试，不要再问“怎么启动”，也不要写模拟接口。最后给出前端地址（`aspire describe` 里 web 资源的 `https://localhost:端口`）。
- Windows 本机没有 .NET 10 SDK、Docker、Aspire，只用于编辑；完整系统一律在 WSL 发行版 `Ubuntu-E` 里用 aspire 启动。项目副本在 WSL 的 `~/openerp`，从 `/mnt/d/git/openerp-main/openerp-main/` rsync 同步（排除 `.git`、`node_modules`、`bin`、`obj`）。
- 改代码不需要手动打包：前端改完同步即热更新；后端改完用 `accept.cmd api` 即可（自动编译检查、重启、冒烟检查）。
- 启动：把下面的脚本放在仓库外（如 `D:\git\openerp-main\wsl_restart.sh`），用 `MSYS_NO_PATHCONV=1 wsl -d Ubuntu-E -- bash /mnt/d/git/openerp-main/wsl_restart.sh` 执行（在 Git Bash 里必须加 `MSYS_NO_PATHCONV=1`，否则 `/mnt/...` 会被改写成 Windows 路径）；首次约 3–5 分钟，放后台运行。脚本里的 `dotnet build` 同时是后端的编译检查，编译错误要先修再交付。

  ```bash
  export DOTNET_ROOT=$HOME/.dotnet
  export PATH=$HOME/.local/bin:$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/node/bin:$PATH
  cd ~/openerp
  aspire stop
  rsync -a --delete --exclude '.git' --exclude 'node_modules' --exclude 'bin' --exclude 'obj' /mnt/d/git/openerp-main/openerp-main/ ~/openerp/
  dotnet build OpenERP.Api.Tests 2>&1 | tail -8
  aspire start
  aspire describe
  ```

- 本地开发端口固定：前端 `https://localhost:5173`、API `https://localhost:5201`、Aspire 控制台 `https://localhost:17000`，启动命令不要加 `--isolated`（会随机端口并隔离机密）。快速验收用仓库外的 `D:\git\openerp-main\accept.cmd [web|api]`（`wsl_accept.sh`）：只改前端用 `web`（同步，Vite 热更新，不重启）；改了后端用 `api`（编译检查、重启、冒烟检查）。
- **例外：如果有人（用户或其他会话）正在同步或修改代码，不要停止、重启或覆盖正在运行的系统。** 此时只报告改动已就绪、尚未重启，由对方决定何时同步。判断依据：`aspire describe` 里有近期被他人启动的实例，或 `~/openerp` 与 Windows 侧的差异不是自己造成的。
- dev 管理员密码在 .NET User Secrets 里，不要猜测或输入，由用户自己登录。
- **接入旺销王（AccessToken）：** 调平台接口、同步订单、打印面单等一切联调，都依赖已绑定的旺销王 AccessToken，核心调用方法是 `IPlatformAgentApiClient.InvokeAsync`（说明见 [平台接口调用流程](docs/development.md#平台接口调用流程)）。开始联调前先确认是否已绑定（登录后 `GET /api/erp/order-sync-availability` 的 `configured`，或页面顶部栏的「API权限绑定」）。**未绑定时，不要自己去数据库或容器里找 Token**，请开发者二选一：
  1. 在对话里把 AccessToken 告诉 AI，由 AI 通过 `PUT /api/erp/config` 绑定；
  2. 自己登录后打开 `/system/erp-binding`（`https://localhost:5173/#/system/erp-binding`）填入并保存。

  绑定之后的开发、联调和验证都由 AI 完成，开发者不需要再手动操作。
- 前端依赖变更后，`pnpm-lock.yaml` 要在 WSL 用 pnpm 11 重新生成（`rm pnpm-lock.yaml; pnpm install --lockfile-only`），不要放宽 pnpm 的供应链策略。

## 验收与 PR

- 在任务回复中报告实际验证结果和未验证项。临时验收产物禁止提交到 Git。
- **临时文件一律放仓库根目录的 `temp/`（已加入 `.gitignore`）**：联调打出的真实面单 PDF、探针日志、截图、导出数据等，不要散落在仓库外或其他目录；用完可整体删除。
- CI 或测试改动须先在本地完成验收，包括验证失败归因，再提 PR。Review 问题修复并验证后才回复和 resolve。
- 仅由人类开发者合并 PR，合并前须确认 PR head 的所有状态检查通过。

## 文档分工

README 面向最终用户，写产品用途、功能和使用入口，贡献指南写共同约定，`docs/` 写操作与技术说明。行为变更时核对相关文档。本文件保留长期代理约定，专项流程放在 `.agents/skills/`，阶段计划、进度和验收记录放在任务、Issue 或 PR。
