# OpenERP

OpenERP 是一个基于 Vue 和 ASP.NET Core 的 ERP 项目，目前支持从私有 ERP 查询 Temu 半托管跨境（TemuPop）店铺，并将历史订单同步到本地 PostgreSQL。

前端使用 Vue 和 TypeScript，后端使用 ASP.NET Core Minimal API 和 EF Core；Aspire 统一启动前后端、PostgreSQL、Redis 和 SeaweedFS，并提供日志与追踪面板。

项目提供 Aspire 本地 Development 环境和 Docker Compose Production 单容器部署。业务页面与 API 需要管理员登录；历史订单同步和文件存储 API 在 Development 与 Production 均可用。Production 发布流程、密钥、代理、数据卷和迁移说明见[部署指南](docs/deployment.md)。

## 功能

- **管理员账户**：单管理员登录、个人资料编辑、修改密码和退出；支持多设备登录，固定 24 小时会话。
- **店铺查询**：分页查看当前 ERP Token 可见的 TemuPop 店铺及平台授权状态，支持失败重试。
- **历史订单同步**：在店铺页手动同步私有 ERP 中已有的订单，查看进度以及新增、更新、未变化和跳过计数。订单保存在 PostgreSQL，重复同步会更新已有记录。
- **定时同步**：在店铺页按固定周期（默认 30 分钟）尝试自动同步选中的店铺；单店失败不影响其他店铺，下一周期会重新扫描。
- **文件存储**：通过 SeaweedFS 提供 S3 兼容存储及文件上传、下载 API。

店铺和订单功能需要 [私有 ERP](https://v3-api.wxwerp.com) 的 Open API Token。未配置 Token 时仍可启动项目，店铺页会提示配置。

## 快速开始

以下步骤面向本地 Development 环境；Production 单容器部署请按[部署指南](docs/deployment.md)操作。

### 环境准备

- 正式版 .NET **10** SDK，[global.json](global.json) 选择已安装的最高 .NET 10 SDK。
- Node.js **26.13.0**，见 [.node-version](.node-version)。
- pnpm **11.16.0**，见 [package.json](OpenERP.Web/package.json)。
- PATH 中可直接调用的 Aspire CLI，版本与项目的 **13.6.5** 对齐；安装方式见 [Aspire 官方安装说明](https://aspire.dev/get-started/install-cli/)。
- 已启动的 Docker Desktop 或 Aspire 支持的兼容容器运行环境。

在仓库根目录安装依赖并信任开发证书：

```bash
dotnet tool restore
aspire certs trust
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

本文直接调用 PATH 中的 `aspire`。`dotnet tool restore` 还原仓库本地工具（包括 EF Core 工具），不替代 PATH 中 Aspire CLI 的安装。首次信任证书可能需要系统确认，已信任后无需重复操作。

### 初始化管理员

本地 Development 首次启动无需手工配置密码。Aspire 自动生成随机临时密码并保存在 User Secrets：在 Dashboard 打开 `api` 的资源详情，在环境变量中查看并复制 `Authentication__InitialPassword`（秘密值默认隐藏）。Production 容器首次初始化使用密钥目录中的 `admin-initial-password` 文件，详见[部署指南](docs/deployment.md#准备密钥与反向代理)。

使用 `admin` 与临时密码登录后，Web 会要求设置自己的新密码（6–20 个字符，不能与临时密码相同）。完成后重新登录即可访问业务；改密前业务页面和 API 均不可用。

临时密码仅用于数据库首次创建管理员；重复启动不会覆盖已修改的密码或资料。已有账户升级后继续使用原密码。忘记密码时使用[管理员密码重置命令](docs/development.md#管理员账户与会话)，修改初始化 Secret 不会重置密码。

### 配置 ERP Token

本地 Development 需要使用店铺和订单功能时执行：

```bash
dotnet user-secrets set "Parameters:wxw-erp-access-token" "你的Token" --project OpenERP.AppHost
```

Token 保存在 .NET User Secrets 中，仅供后端使用，不会发送到浏览器或写入 Git。修改后需重新启动应用。Production 请把 Token 放入密钥目录中的 `erp-access-token` 文件。

### 启动并访问

在仓库根目录后台启动；Git worktree 或需要与其他本地实例分开运行时使用隔离模式：

```bash
aspire start --isolated
```

仓库的 `aspire.config.json` 已指定唯一 AppHost，无需重复指定项目。`--isolated` 隔离运行状态，不等于创建独立测试数据库或清空数据卷。普通后台启动可用 `aspire start`；需要终端前台运行时可用 `aspire run`。

也可以在安装了 Aspire 扩展的 VS Code 中，选择仓库提供的 `apphost` 启动配置运行；需要 worktree 隔离时使用上面的 CLI 入口。

Aspire 会自动启动前端及依赖服务。打开启动输出中的 Dashboard 登录链接，等待 `web` 就绪后点击其 HTTPS 地址；端口动态分配，以 Dashboard 显示为准。首次使用按上文完成强制改密，再用新密码登录；首页显示 `pong` 表示前后端已连通。Dashboard 登录与应用管理员登录相互独立。

进入 `/shops` 查看店铺，点击「同步历史订单」即可导入该店铺的历史订单。也可在「定时同步」卡片启用后按固定周期自动同步选中的店铺。平台授权失效不妨碍读取私有 ERP 中已有的订单，但 ERP Token 仍需有权访问该店铺。后台同一时间执行一个同步任务；刷新页面可恢复最近任务的状态，API 重启后定时同步会在下一个周期重跑，批次结束后也可手动补跑。

同步保留本次未返回或被跳过的已有订单。源端数据在分页期间变化时仍可能漏读，详见[订单同步边界](docs/development.md#订单同步边界)。

### 停止与数据保留

通过 `aspire start` 启动的后台实例，在同一仓库或 worktree 根目录运行 `aspire stop` 停止；关闭启动终端不会停止后台实例。终端前台 `aspire run` 按 `Ctrl+C` 停止；通过 VS Code 启动时使用编辑器的停止按钮。普通停止不要使用 `--force`，它会删除持久资源。

仅修改后端代码时，可在 Dashboard 重建 `api`，无需停止整个应用，见[更新运行中的代码](docs/development.md#更新运行中的代码)。

PostgreSQL、Redis 和 SeaweedFS 使用持久容器与 Docker 数据卷，正常停止应用后保留开发数据。可在 Docker Desktop 的 Volumes 页面查看数据卷。不要随意删除数据卷、开发密钥或更改 `UserSecretsId`，否则会丢失数据或导致已有数据无法连接。

## 参与开发与帮助

贡献要求和提交约定见[贡献指南](CONTRIBUTING.md)。代码导航、构建测试、接口行为和同步限制见[开发说明](docs/development.md)，启动或请求异常时参考其中的[本地诊断](docs/development.md#本地诊断)。Production 发布与恢复见[部署指南](docs/deployment.md)。问题反馈请使用 [GitHub Issues](https://github.com/wxwerp-org/openerp/issues)。

## 许可证

[Apache License 2.0](LICENSE)
