# 仓库贡献指南

## 语言与协作要求

- 全程使用中文沟通；文档和代码注释必须使用中文。命令、路径、标识符及专有名称保留原样。
- 准确遵循用户要求，保持改动最小；涉及实质性范围或兼容性取舍时，先确认再实施。

## 项目结构

当前仓库是开发骨架，尚无业务实体或业务流程。

- `OpenERP.AppHost/`：通过 Aspire 编排前端、API、PostgreSQL 和 Redis。
- `OpenERP.Api/`：ASP.NET Core Minimal API 与 EF Core `AppDbContext`。
- `OpenERP.ServiceDefaults/`：共享健康检查、遥测和服务发现配置。
- `OpenERP.Web/src/`：Vue/TypeScript 前端；`router/` 定义路由，`views/` 存放页面，`styles.css` 提供共享样式；尚无独立静态资源目录。
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
dotnet tool run aspire start --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

Aspire 自动启动前端。端口动态分配，请通过 Dashboard 打开 `web` 地址。停止时，将启动命令中的 `start` 替换为 `stop`。

代理执行 .NET `restore`、`build` 必须直接使用沙箱外权限；`run`、`test` 涉及隐式或显式还原时同样适用。

## 编码风格与命名

遵循 `.editorconfig`：使用空格，C# 缩进四格，JSON/XML 缩进两格；采用文件范围命名空间，类型和方法使用 PascalCase，接口以 `I` 开头，私有实例字段使用 `_camelCase`。

C#/.NET 后端处理可预期失败时优先使用 Result 模式；适用时优先采用 `RokyZevon.OpResult`，避免用异常控制正常流程。

前端沿用两格缩进、单引号、不加分号、PascalCase Vue 文件名及 `<script setup lang="ts">`。TypeScript 已开启严格检查，尚未配置 ESLint 或 Prettier。

## 测试与验证

后端测试项目 `OpenERP.Api.Tests` 使用 xUnit v3 + 原生 MTP，并已引入 Aspire.Hosting.Testing。前端采用 Vitest + Vue Test Utils + jsdom，浏览器采用 TypeScript + Playwright Test；配置及运行方式见 `README.md`。当前保留已有文件存储测试，尚未新增前端或浏览器用例，也没有覆盖率门槛。明确记录实际验证结果及未验证项。

## 提交与合并请求

现有提交采用 `feat: ...`、`chore(aspire): ...` 等 Conventional Commits 格式。提交应聚焦单一目的。合并请求说明应包含行为变化、关联工单或问题、验证结果；界面变更附截图。

## 配置与数据保护

密钥保存在 .NET User Secrets，禁止写入 Git。保留依赖锁文件和持久开发数据卷；不要随意重置 UserSecretsId、删除开发密钥或数据卷。
