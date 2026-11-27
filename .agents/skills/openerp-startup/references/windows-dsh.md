# 在 Windows DSH 中启动 OpenERP

受影响的 `windows-acl` 后端在 `workspace-write` 授予工作区写权限时，会留下可继承的 Windows 低完整性标签（`Low Mandatory Level`）。
只读 shell 命令也可能触发授权，切换模式或退出 DSH 不会清除标签。
用 DSH 的 `read` 工具读取项目文档。 [DSH 上游复现](https://github.com/deepseek-ai/deepseek-harness/discussions/8412)

1. **先选执行入口。** 从已有会话信息判断。工作区命令使用已获准且不会写入此标签的本机执行入口。
   若 `pwsh` 已拒绝所需命令并提供单次审批，只对原命令申请 `danger-full-access` 重试。
   [单次审批](https://github.com/deepseek-ai/deepseek-harness/blob/master/packages/shell/tool-pwsh/README.md#windows-specific-sandbox-behavior)
   若只能使用 DSH 的「完全权限」（Full access）预设，先说明它会放宽当前整个会话的沙箱限制。
   由用户在当前会话的权限菜单决定是否切换。 [DSH 权限选择器](https://github.com/deepseek-ai/deepseek-harness/blob/master/packages/client/ui-permission-presets/README.zh.md#use-this-package)
   没有可用入口时停在命令执行前，由助手说明需要的授权。
2. **优先接续原实例。** 依照 [Aspire 编排 Skill](../../aspire-orchestration/SKILL.md) 复用同项目、同运行方式的实例。
   需要启动且原目录没有已知标签问题时，按[开发说明](../../../../docs/development.md#启动与停止)直接用 `aspire start`。
   无 Docker 时**必须**显式传入 `--launch-profile native`。
3. **回连套接字绑定报 `10013` 时**，通过已获准的入口，用不带修改参数的 `icacls` 检查原工作区和本次启动的 AppHost 可执行文件。
   Aspire 的回连套接字使用文件路径。继承了低完整性标签的 AppHost 即使换终端，也可能无法写入套接字的父目录。
   `10013` 只表示访问被拒绝。没有发现相关标签时，继续按 [Aspire 诊断 Skill](../../aspire-monitoring/SKILL.md) 排查。
   [Windows 强制完整性控制](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)
4. **原目录受标签影响且没有可复用实例时**，通过同样不会写标签的获准入口，在受影响目录树之外重新解压同一份源码或检出同一版本。
   换目录前先核对本地改动和[实际数据位置](../../../../docs/development.md#开发数据与密钥)。
   新目录默认使用自己的 `.openerp/native/` 或 Docker 卷，未提交的改动需要单独带入。
   需要沿用原数据时，先落实数据和密钥的保留方式，再启动新实例。
   `native` 数据包括 SQLite 和附件目录，Docker 的业务数据包括 PostgreSQL 和 SeaweedFS。
   按用户已授权的方式保留或迁移数据。用空实例替代有数据的实例须经用户明确同意。
   保留原目录、卷和密钥，通过获准入口按原运行方式启动。

启动后按[主指南](../SKILL.md)验证页面并交付地址。若换目录仍然失败，按本次 Aspire 日志继续排查。命令由助手执行。
