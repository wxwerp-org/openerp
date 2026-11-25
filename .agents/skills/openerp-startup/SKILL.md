---
name: openerp-startup
description: >-
  **WORKFLOW SKILL** — 引导助手在本机启动、验证或继续使用 OpenERP 时，正确与用户交流、选择运行方式和选择要读的文档。
  不重复 Aspire Skill 的 CLI 细节；启动、停止、等待、诊断和资源操作按项目内 Aspire Skill 执行。
  USE FOR: "帮我把项目跑起来"、"启动 OpenERP"、"跑起来看看"、"没有 Docker"、
  "Docker 下载不了项目需要的文件"、"用无 Docker 的方式"、"把这个版本留在本机"。
  DO NOT USE FOR: 生产部署、镜像发布、普通功能开发（除非为验证启动）、Aspire AppHost 实现。
  INVOKES: aspire, aspire-orchestration, aspire-monitoring, playwright-cli。
---

# OpenERP 本机启动指南

## 这个指南负责什么

用户说「帮我把项目跑起来」时，助手负责把 OpenERP 在这台电脑上启动起来，确认实际页面能打开，并给出可点击的地址。用户不需要自己查文档或执行命令。

本指南是这件事的统一入口，不是 native 专属。它只负责三件事：

1. **与用户交流**：说明两种运行方式、征得安装同意、解释 Docker 失败、交付地址、提醒数据风险。
2. **选择运行方式**：无 Docker（native profile）还是 Docker（默认 https profile）。
3. **选择下一步读什么**：Aspire Skill、开发说明、部署说明、浏览器验证 Skill。

本指南不重写 Aspire CLI 的用法、guardrail、等待/停止/资源检查细节；这些以项目内 Aspire Skill 和开发说明为准。

## 下一步读什么

在 Windows 上用 DeepSeek Harness（DSH）启动 OpenERP 时，先用 DSH 的 `read` 工具阅读
[Windows DSH 启动指引](references/windows-dsh.md)，再执行工作区命令。
如果项目曾在 Windows DSH 中使用，且 AppHost 绑定回连套接字时报 `10013`，也按此指引检查。

| 需要做的事 | 读什么 |
| --- | --- |
| 启动、停止、等待、资源操作、判断实例归属 | [项目内 Aspire Skill](../aspire/SKILL.md)，按路由读[编排](../aspire-orchestration/SKILL.md) |
| 看日志、状态、健康、诊断 | [Aspire 诊断 Skill](../aspire-monitoring/SKILL.md) |
| 用浏览器确认页面 | [playwright-cli](../playwright-cli/SKILL.md) |
| 启动命令、环境准备、数据位置、配置、重建 | [开发说明](../../../docs/development.md)，重点见[启动与停止](../../../docs/development.md#启动与停止)、[更新运行中的代码](../../../docs/development.md#更新运行中的代码)、[开发数据与密钥](../../../docs/development.md#开发数据与密钥) |
| 生产部署、镜像发布 | [部署说明](../../../docs/deployment.md)；不要把它当成本机发布说明 |
| 用户入口和功能说明 | [README](../../../README.md) |

如果项目内 Aspire Skill 与当前安装的 Aspire 插件 Skill 冲突，按仓库 `AGENTS.md` 的约定优先项目内 Skill，并把版本相关差异告诉用户。

## 先判断用哪种方式

两种方式都在这台电脑上运行，都由助手操作：

- **无 Docker 方式（native profile）**：API 和前端作为本机进程运行，使用内嵌 SQLite、进程内 HybridCache 和本机文件系统附件。不需要 Docker Desktop 或 WSL。用户无法判断时推荐这种方式。
- **Docker 方式（默认 https profile）**：沿用同一个 AppHost，使用 PostgreSQL、Redis 和 SeaweedFS。团队开发通常使用这种方式；macOS/Linux 上也可以使用 Aspire 支持的兼容容器运行环境，不限于 Docker Desktop。

用户没有明确选择时，先用普通用户能理解的话说明两种方式，再请用户选择。Windows 11 上按下面原文说：

- **在这台电脑上试用或改一改（推荐非技术用户）**。我会帮你把项目跑起来，也可以帮你改代码。选这种方式，可以省去 Docker Desktop 和 WSL 这两项软件。
- **按团队的方式开发（推荐技术用户）**。我会帮你准备团队使用的开发环境。这台电脑需要安装 Docker Desktop 并启用 WSL，关掉项目后它们仍会占用硬盘。

上面的第一项对应无 Docker 方式，第二项对应 Docker 方式。macOS 或 Linux 上把第二项中的「Docker Desktop 并启用 WSL」改成「Docker Desktop 或 Aspire 支持的兼容容器运行环境」，仍然说明它会长期占用硬盘；第一项相应改为「可以省去安装 Docker」。

用户已经明确选择时直接照办，不要反复询问。用户明确说不用 Docker 时，直接走无 Docker 方式，不再追问 Docker。用户说「都行」「你决定」或无法判断时：只想试用或非技术用户选无 Docker 方式；明确需要 Docker 或团队开发所需的容器环境时选 Docker 方式；仍不确定时选无 Docker，并说明可以随时切换。

两种方式的数据分开保存：从无 Docker 方式切换到 Docker 方式（或反向）不会自动迁移账号、订单、设置和附件，也不会删除另一方式已有的数据。需要迁移时另行处理。

## 与用户交流的节点

- **安装工具前**：按[环境准备](../../../docs/development.md#环境准备)和 Aspire Skill 的 Prerequisites 检查 .NET SDK、Aspire CLI、Node.js、pnpm；复用兼容版本，只安装缺少的。任何工具安装、全局包变更或证书信任，都要先说明改什么、为什么、有什么影响，得到确认后再执行；需要系统权限时提醒 UAC 或管理员确认。
- **Docker 已存在时**：直接复用，不重装、不升级。先确认 Aspire 实际选中的容器运行时；不要因为 `docker` 命令不存在就判定没有容器环境，兼容运行时按 Aspire 的检查处理。
- **镜像拉取失败时**：不要反复重试或改镜像源/代理。先看启动输出和 Aspire Skill 的诊断入口，用这句话向用户说明：

  > 这台电脑上的 Docker 下载不了项目需要的文件。

  然后询问是否改用无 Docker 的方式；用户同意后再切换。Docker 守护进程没运行不等于 Docker 不可用，先请用户启动它。
- **页面验证时**：通过 Aspire Skill 取得 Web 地址，用真实浏览器确认页面实际渲染。首次启动的初始化页、登录页和工作台都算正常；只看到 HTTP 200、空白页、证书错误页或服务错误页不算成功。没有可用浏览器时如实说明限制，请用户打开地址确认，不要宣称已经验证。
- **首次使用时**：让用户按页面提示设置管理员密码，再用 `admin` 登录。不要替用户决定密码，也不要把密码写进日志、聊天或提交内容。
- **交付地址时**：给出完整、可点击的地址，并说明首次使用要先设置管理员密码。
- **涉及数据时**：无 Docker 方式的数据在 `.openerp/native/`；Docker 方式的数据在 Docker 卷中，开发密码等密钥在 .NET User Secrets 中。保留卷时也要保留对应密钥。不要删除 `.openerp/`、Docker 卷或密钥，除非用户明确要求删除数据并确认影响；需要彻底重置时先列明会丢失的账号、订单、设置和附件。
- **平台不同时**：先识别 Windows 11、macOS 或 Linux，再按对应平台的说明和措辞交流。只有 Docker 方式才涉及 Docker Desktop 和 WSL；不要把未验证平台的行为说成已验证。Windows 使用 PowerShell 或 CMD，具体命令和安装入口以开发说明与 Aspire Skill 为准；仓库尚未记录 Windows 实机验收。

## 选择命令

本指南只做选择，不复制 Aspire Skill 和开发说明里的命令细节。需要执行时，按上表读对应文档或 Skill。

| 时机 | 选择 | 细节在哪里 |
| --- | --- | --- |
| 无 Docker 启动 | `native` profile | [开发说明的启动与停止](../../../docs/development.md#启动与停止)；代理参数按 Aspire 编排 Skill |
| Docker/团队启动 | 默认 `https` profile | 同上；容器运行时按 Aspire Skill 确认 |
| 已有同方式实例 | 复用，不重复启动 | [Aspire 编排 Skill](../aspire-orchestration/SKILL.md) |
| 已有另一种方式实例 | 先问用户是否切换 | Aspire 编排 Skill + 本文的数据说明 |
| 页面验证 | [playwright-cli](../playwright-cli/SKILL.md) | 本文的交付要求 |
| 启动失败、日志、状态 | [Aspire 诊断 Skill](../aspire-monitoring/SKILL.md) | 见 Skill |
| 改后端 C# 代码 | 重建资源，不用 Restart 代替 | [更新运行中的代码](../../../docs/development.md#更新运行中的代码) |
| 停止 | 按 Aspire 编排 Skill | 见 Skill；先确认实例归属 |
| 把版本留在本机 | 当前仓库没有本机发布入口 | 如实说明，不自行发布 |

通过 Aspire 启动时，前端依赖由 AppHost 的 `web-installer` 安装，不要先手工执行 `pnpm install`；只有单独运行前端工具或排查依赖时，才按开发说明手工安装。

## 把版本留在本机

用户说「把这个版本留下来，以后就在这台电脑上用」时：

1. 核对仓库是否提供本机发布入口：查看 `docs/` 中的发布说明或专门的发布文档；不要把生产部署说明或 `docs/deployment.md` 当成「发布到本机」入口。
2. 如果找不到，如实说明当前版本还不支持，不要自行用 `dotnet publish`、脚本、安装包、桌面快捷方式或启动器替代；仓库新增入口后按新入口执行，并同步更新本指南。
3. 入口存在时按仓库发布文档执行，并遵循：发布物包含网页和 .NET 运行时；第一次发布时询问用户要沿用现有数据还是从空数据开始；更新已发布程序时继续使用原有数据。

本指南不定义发布命令；禁止为了让用户直接打开而新增启动器、安装器或包装脚本。

## 示例措辞

首次说明：

> 两种方式都在这台电脑上运行，都由我来操作。你可以选：
> - 在这台电脑上试用或改一改（推荐非技术用户）。我会帮你把项目跑起来，也可以帮你改代码。选这种方式，可以省去安装 Docker Desktop 和 WSL。
> - 按团队的方式开发（推荐技术用户）。这台电脑需要安装 Docker Desktop 并启用 WSL，关掉项目后它们仍会占用硬盘。

macOS/Linux 上引用这段时，把第二项改为「需要安装 Docker Desktop 或 Aspire 支持的兼容容器运行环境，关掉项目后它仍会占用硬盘」，第一项改为「可以省去安装 Docker」。

镜像下载失败：

> 这台电脑上的 Docker 下载不了项目需要的文件。要不要我改用不需要 Docker 的方式在这台电脑上运行？

启动完成：

> 已经启动并验证页面：【可点击地址】。首次使用请先设置管理员密码，再用 admin 登录。

本机发布暂不可用：

> 当前版本还没有提供「把版本留在本机」的发布流程。我不会自己做一个启动器或安装包；可以继续从源码运行，等仓库提供该入口后再发布。

## 禁止事项

- 不擅自切换运行方式，不把「Docker 失败」直接当成「改用无 Docker」而不问用户。
- 不重复安装或升级用户已有的工具和依赖。
- 不在未说明、未获确认时安装工具、修改全局包或信任证书。
- 不替用户决定管理员密码，不打印、记录或提交密码、Token、连接字符串等秘密。
- 不删除 `.openerp/`、Docker 卷或 User Secrets，不用强制停止/清理命令处理普通停止。
- 不编造命令、端口、地址、版本或「已经成功」的结论。
- 不新增启动器、安装器、桌面快捷方式或包装脚本。
- 不把未验证的平台、方式或发布流程说成已验证。
- 不重复 Aspire Skill 已经写明的 CLI 细节；需要时按路由读取对应 Skill 或开发说明。
