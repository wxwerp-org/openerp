# OpenERP

OpenERP 是一个基于 Vue 和 ASP.NET Core 的 ERP 项目，目前支持从私有 ERP 查询 Temu 半托管跨境（TemuPop）店铺，并将历史订单同步到本地 PostgreSQL。

前端使用 Vue、TypeScript 和 Element Plus，后端使用 ASP.NET Core Minimal API 和 EF Core；Aspire 统一启动前后端、PostgreSQL、Redis 和 SeaweedFS，并提供日志与追踪面板。

**目前用于本地开发，尚未提供登录与访问控制。** 历史订单同步和文件存储 API 仅在 Development 环境开放。

## 功能

- **店铺查询**：分页查看当前 ERP Token 可见的 TemuPop 店铺及平台授权状态，支持失败重试。
- **历史订单同步**：在店铺页手动同步私有 ERP 中已有的订单，查看进度以及新增、更新、未变化和跳过计数。订单保存在 PostgreSQL，重复同步会更新已有记录。
- **定时同步**：在店铺页按固定周期（默认 30 分钟）尝试自动同步选中的店铺；单店失败不影响其他店铺，下一周期会重新扫描。仅 Development 开放。
- **开发文件存储**：通过 SeaweedFS 提供 S3 兼容存储及文件上传、下载 API。

店铺和订单功能需要 [私有 ERP](https://v3-api.wxwerp.com) 的 Open API Token。未配置 Token 时仍可启动项目，店铺页会提示配置。

## 快速开始

### 环境准备

- 正式版 .NET **10** SDK，[global.json](global.json) 选择已安装的最高 .NET 10 SDK。
- Node.js **26.13.0**，见 [.node-version](.node-version)。
- pnpm **11.16.0**，见 [package.json](OpenERP.Web/package.json)。
- 已启动的 Docker Desktop 或 Aspire 支持的兼容容器运行环境。

在仓库根目录安装依赖并信任开发证书：

```bash
dotnet tool restore
dotnet tool run aspire certs trust
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

Aspire CLI 由仓库工具清单固定版本，无需全局安装。首次信任证书可能需要系统确认，已信任后无需重复操作。

### 配置 ERP Token

需要使用店铺和订单功能时执行：

```bash
dotnet user-secrets set "Parameters:wxw-erp-access-token" "你的Token" --project OpenERP.AppHost
```

Token 保存在 .NET User Secrets 中，仅供后端使用，不会发送到浏览器或写入 Git。修改后需重新启动应用。

### 启动并访问

在终端运行：

```bash
dotnet tool run aspire run --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

也可以在安装了 Aspire 扩展的 VS Code 中，选择仓库提供的 `apphost` 启动配置运行。

Aspire 会自动启动前端及依赖服务。打开启动输出中的 Dashboard 登录链接，等待 `web` 就绪后点击其地址；端口动态分配，以 Dashboard 显示为准。首页显示 `pong` 表示前后端已连通。

进入 `/shops` 查看店铺，点击「同步历史订单」即可导入该店铺的历史订单。也可在「定时同步」卡片启用后按固定周期自动同步选中的店铺。平台授权失效不妨碍读取私有 ERP 中已有的订单，但 ERP Token 仍需有权访问该店铺。后台同一时间执行一个同步任务；刷新页面可恢复最近任务的状态，API 重启后定时同步会在下一个周期重跑，批次结束后也可手动补跑。

同步保留本次未返回或被跳过的已有订单。源端数据在分页期间变化时仍可能漏读，详见[订单同步边界](docs/development.md#订单同步边界)。

### 停止与数据保留

终端前台运行时按 `Ctrl+C` 停止；通过 VS Code 启动时使用编辑器的停止按钮。

PostgreSQL、Redis 和 SeaweedFS 使用持久容器与 Docker 数据卷，正常停止应用后保留开发数据。可在 Docker Desktop 的 Volumes 页面查看数据卷。不要随意删除数据卷、开发密钥或更改 `UserSecretsId`，否则会丢失数据或导致已有数据无法连接。

## 参与开发与帮助

贡献要求和提交约定见[贡献指南](CONTRIBUTING.md)。代码导航、构建测试、接口行为和同步限制见[开发说明](docs/development.md)，启动或请求异常时参考其中的[本地诊断](docs/development.md#本地诊断)。问题反馈请使用 [GitHub Issues](https://github.com/wxwerp-org/openerp/issues)。

## 许可证

[Apache License 2.0](LICENSE)
