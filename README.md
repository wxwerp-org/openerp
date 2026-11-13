# OpenERP

Aspire 编排 Vue/Vite、ASP.NET Core Minimal API、PostgreSQL、Redis 和 SeaweedFS。后端只有一个应用进程，已接入私有 ERP 店铺读取、TemuPop 历史订单手动同步及开发环境文件存储。

## 私有 ERP 店铺（B2）

本期接入 `https://v3-api.wxwerp.com`，仅查询 Temu 半托管跨境（TemuPop）店铺。在仓库根目录设置 Open API Token：

```bash
dotnet user-secrets set "Parameters:wxw-erp-access-token" "你的Token" --project OpenERP.AppHost
```

按下文通过 Aspire 启动后打开 `/shops`。AppHost 将密钥参数注入后端的 `Erp__AccessToken`；前端只读取配置是否存在，不获取 Token，不把 Token 存入浏览器或数据库。修改 Token 后重新启动该 AppHost。直接配置后端时使用环境变量 `Erp__AccessToken`。

店铺页支持加载、重试和分页，逐店显示平台授权有效、无效或查询失败。Token 已配置不代表权限有效；能查询店铺也不代表平台授权有效。授权无效的店铺仍可显示，历史订单是否可读另行核验。当前没有管理员登录，部署及访问控制由 C1/C4 完成。

本轮确认 SID 30 用于历史订单联调，SID 644928 用于后续订单分页验证；B2 不触发订单同步或保存。调查记录和接口契约差异见 [B1 #6](https://github.com/wxwerp-org/openerp/issues/6)。

后端入口：`GET /api/erp/config`、`GET /api/erp/shops?page=1&pageSize=20`，每页 1–100 条。上游列表失败返回错误；单店授权状态查询失败保留店铺并标记未知。返回的页面数量和总数来自 ERP。

## 历史订单同步（B3）

在开发环境的 `/shops` 选择店铺，点击「同步历史订单」。只读取当前 ERP AccessToken 可见的 TemuPop 店铺在私有 ERP 中已有的历史订单，平台授权失效仍可使用。后台同一时间执行一个同步，页面展示进度及新增、更新、未变化和跳过计数。刷新页面可查看最近一次任务，API 重启后可手动重跑。

`GET /api/erp/config` 在开发环境返回 `orderSyncEnabled: true`，店铺页据此显示同步入口并查询任务。开发 API 使用 `POST /api/erp/shops/{sid}/order-sync` 受理任务，返回 `202`，已有同步时返回 `409`。同路径 `GET` 查询最近任务，无任务返回 `204`。

API 启动时应用 EF Core 迁移。PostgreSQL 以 ERP 内部 ID 保存主单，平台 PO 和商品行分别关联主单。订单时间保留 ERP 显示原值及来源。同步刷新本轮核对通过的订单，保留本轮未返回或归属异常而被跳过的订单。完整读取后才写库，归属异常显示为部分成功。当前销量口径由 [B4 #9](https://github.com/wxwerp-org/openerp/issues/9) 跟踪。

订单列表按 ERP ID 排序并核对前后计数、每页条数和 ID 唯一性。偏移分页读取期间，如果不同订单同时进入和退出可见范围，前后总数可能相同，此时仍可能漏读。两种页长的验收结果覆盖当次源数据。

## 环境

- .NET SDK **10.0.404**（`global.json`）；本地 Aspire CLI **13.6.5**（工具清单）。
- Node.js **26.13.0**（`.node-version`）、pnpm **11.16.0**。
- 已启动的 Docker Desktop，或 Aspire 支持的兼容容器运行环境。
- 已受信任的 ASP.NET Core HTTPS 开发证书；浏览器日志需要 Chrome 或 Edge。

主要依赖：Vue 3.5.45、Vue Router 5.5.0、Element Plus 2.14.8、Vite 8.3.8、TypeScript 6.0.3、EF Core 10.0.15、Npgsql EF Provider 10.0.3。Aspire 官方集成为 13.6.5；SeaweedFS 使用 `CommunityToolkit.Aspire.Hosting.SeaweedFS 13.6.1`；浏览器日志是预览包：`Aspire.Hosting.Browsers 13.6.5-preview.1.26559.7`。

容器镜像标签为 `postgres:18`、`redis:8` 和 SeaweedFS `4.53`。PostgreSQL 与 Redis 允许同一大版本内更新；2026-11-12 验证时分别为 PostgreSQL 18.6、Redis 8.10.3。二者未固定小版本或 digest，也不使用跨大版本的 `latest`。本机已有镜像可能被缓存，升级时应先拉取对应标签，再重启 AppHost。

## 启动

在仓库根目录执行首次准备：

```bash
dotnet tool restore
dotnet tool run aspire certs trust --non-interactive
dotnet restore OpenERP.slnx --locked-mode
pnpm --dir OpenERP.Web install --frozen-lockfile
```

证书已受信任时无需重复信任；首次操作可能出现系统钥匙串确认。

AppHost 的开发启动配置让 `localhost`、`127.0.0.1`、`::1` 绕过 HTTP 代理，避免 Dashboard 到 AppHost 的本地 gRPC 连接被系统代理转发；不会修改全局代理设置。

在 VS Code 中进行人工本地开发时，继续使用 VS Code 的本地 Aspire 编排。代理在 Git worktree 中使用下面的锁定本地 CLI 命令和隔离状态：

```bash
dotnet tool run aspire start --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive --isolated
```

Aspire 会启动前端，无需另外执行 `pnpm dev`。可通过命令输出的登录链接打开 Dashboard 观察状态，无需等待 `web`；访问资源列表中的 `web` 地址前，先执行下面的等待命令。端口动态分配，启动配置中的 `0` 表示自动分配，不是固定访问端口。

```bash
dotnet tool run aspire wait web --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
dotnet tool run aspire describe --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

## 验证与诊断

- Vue Router 的 `/` 页面显示后端响应 `pong`，`/shops` 页面显示私有 ERP 店铺；首页可点击重试。浏览器请求相对路径 `/api/ping`，Vite 将其转发到 Aspire 注入的 API 地址。开发代理仅为本地 HTTPS 后端接受自签证书。
- Dashboard 中检查 `postgres`、`openerp`、`redis`、`seaweedfs`、`api`、`web` 的状态。Toolkit 默认检查 SeaweedFS Master/Volume；本项目为启动前的建桶初始化额外增加签名 S3 `ListBuckets` 检查，API 启动会等待两者成功。这是项目的启动保护，不是 Aspire 通用要求，也不保证后续建桶必定成功。`WaitFor` 不会因存储后续失去健康而暂停已运行的 API。Development 环境下，API 的 `/health` 会检查 EF Core 数据库连接、Redis 客户端和 SeaweedFS S3 存储（列举 bucket）；`/alive` 只检查应用进程存活。浏览器日志位于 `web-browser-logs` 子资源。
- Dashboard 中打开 API 的结构化日志与追踪，查看 `OpenERP connectivity check received`。浏览器日志通过 `web-browser-logs` 子资源查看。

```bash
dotnet tool run aspire otel logs api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire otel traces api --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire resource web-browser-logs open-tracked-browser --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
dotnet tool run aspire logs web-browser-logs --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

资源名以 `aspire describe` 的实际输出为准。`.mcp.json` 和 `.vscode/mcp.json` 保留 `aspire agent init` 生成的 `aspire agent mcp` 配置，MCP 客户端环境的 PATH 需能找到 Aspire CLI；代理执行仓库 CLI 命令仍使用锁定的 `dotnet tool run aspire` 入口。这些官方 CLI 命令也提供本地诊断路径。

独立构建，不要求 API 或容器正在运行：

```bash
dotnet build OpenERP.slnx --no-restore
pnpm --dir OpenERP.Web typecheck
pnpm --dir OpenERP.Web build
```

## 开发数据与停止

PostgreSQL、Redis 和 SeaweedFS 均使用 Docker 数据卷及持久容器生命周期。卷由 Aspire 根据应用身份与资源名生成，保存在 Docker 管理的存储中；macOS Docker Desktop 下位于其 Linux 虚拟机内，不在源码目录。可在 Docker Desktop 的 Volumes 页面查看。

PostgreSQL 数据卷挂载到 `/var/lib/postgresql`，Redis 和 SeaweedFS 数据卷分别挂载到各自容器的 `/data`。卷名包含 checkout 对应的应用身份，具体名称以当前环境为准，可在 Docker Desktop 的 Volumes 页面查看。

AppHost 首次运行自动生成开发密码并保存在 .NET User Secrets 中，不写入 Git。使用以下命令查看当前开发密钥文件的位置，避免打印秘密值：

```bash
dotnet tool run aspire secret path --apphost OpenERP.AppHost/OpenERP.AppHost.csproj
```

正常停止：

```bash
dotnet tool run aspire stop --apphost OpenERP.AppHost/OpenERP.AppHost.csproj --non-interactive
```

正常停止 AppHost 后，持久数据库容器和数据卷会保留，可在下次启动时复用。不要随意重置 UserSecretsId 或删除开发密钥，否则保留的数据卷可能与新密码不匹配。删除数据卷会丢失开发数据；`aspire stop --force` 不是普通停止命令。

Redis 使用官方数据卷集成启用的快照持久化；尚无缓存业务，也不承诺异常断电时保存最近的内存写入。

本次仅用于开发。前端生产镜像及 Caddy、后端生产镜像和部署流水线均未实现。

## 历史订单同步验证

订单读取测试可独立运行。订单 PostgreSQL 和文件存储测试共用测试宿主启动和清理的隔离 Aspire 环境，订单测试为各用例新建并清理独立数据库。真实验收测试在配置 `OPENERP_TEST_ERP_TOKEN` 时对 SID `644928` 只读扫页，另配置 `OPENERP_TEST_POSTGRES` 可核对 SID `30` 的已同步数据。凭证通过进程环境提供。

```bash
dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --filter-class '*OrderReaderTests*'
dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --filter-class '*OrderStorePostgresTests*'
dotnet test --project OpenERP.Api.Tests/OpenERP.Api.Tests.csproj --filter-class '*OrderLiveAcceptanceTests*' --show-test-results passed --show-stdout All
```

### B3 行为验证记录

| 场景 | 红灯 | 绿灯 |
| --- | --- | --- |
| 订单读取与归属校验 | `OrderReaderTests` 的 8 项测试在空实现上全部失败 | 筛选、完整扫页、时间补查、整单跳过和详情失败阻断共 8 项通过 |
| PostgreSQL 初始化与整批保存 | `OrderStorePostgresTests` 中迁移前查到 0 张订单表，存储空实现有 3 项失败 | 迁移、子项更新、乱序重放和数据库回滚共 4 项通过 |
| 店铺页手动入口与状态恢复 | 无同步入口时 GET 返回 404，5 个初始前端用例失败。跨店铺 409 后无法重试的回归用例也先复现失败 | GET 无任务返回 204，前端测试 9 项通过，含跨店铺冲突后的手动重试 |

2026-11-12 隔离 Aspire 验收。SID `30` 在平台授权无效的情况下，浏览器首轮新增 53 单，第二轮 53 单均未变化，刷新后可查看结果。直接读取 PostgreSQL 核对了 53 张主单、53 张平台 PO 和 55 行商品。SID `644928` 按每页 20 和 100 条只读扫描，分别覆盖 113 和 23 页，得到相同的 2,259 个唯一 ERP ID，子单 SID 匹配，跳过 0 单。

最终后端常规回归通过 16 项，另行运行的真实 ERP 验收通过 2 项。前端测试 9 项、类型检查与构建通过。锁定依赖还原和 .NET 解决方案构建通过，既有文件存储测试代码仍有 15 条 `xUnit1051` 构建警告。

#### 2026-11-13 评审修复

| 场景 | 红灯 | 绿灯 |
| --- | --- | --- |
| ERP 已知字段类型错误 | 12 个用例复现解析接纳错误类型并产生空值 | 错误类型使批次失败，合法缺省和空值保留 |
| 补查时间无法区分无时间和主单消失 | 4 个用例复现旧详情仍被接纳 | 复核相同 ERP ID，读取失败则中止整批，归属不符则跳过，正常详情按复核结果保存 |
| 同步进度 | 2 个用例复现详情阶段一直为 99%，提前失败显示 100% | 列表和详情分阶段推进，失败保留实际进度，成功和部分成功显示 100% |

后端订单读取测试 25 项、前端测试 15 项全部通过，.NET 解决方案构建、前端类型检查和构建通过。使用修复后的读取器只读复核 SID `30`，通过 53 张主单、53 张平台 PO、55 行商品。该批订单的创建时间均来自列表，时间补查的异常路径由回归用例验证。

#### PR #26 评审回归

| 场景 | 红灯 | 绿灯 |
| --- | --- | --- |
| Given ERP 已配置，When 店铺页请求配置，Then 仅开发环境声明并提供同步能力 | 后端 3 个环境均缺少能力字段，前端 3 个能力关闭用例仍显示按钮 | HTTP 回归核对配置与路由，前端验证关闭或缺少字段时隐藏入口、停止状态请求与轮询 |
| Given 店铺不可见、已有同步任务或数据库约束拒绝写入，When 同步遇到该错误，Then 返回 OpResult 失败 | 分别得到自定义结果、`null` 和 `DbUpdateException` | 保留失败提示和冲突行为，PostgreSQL 回归核对失败前后的主单与子项版本一致 |
| Given 容器环境与开发证书已就绪，且未设置 `OPENERP_TEST_POSTGRES`，When 运行默认全量测试，Then 测试宿主提供隔离 PostgreSQL，四项订单存储用例执行原有断言 | 四项用例在初始化阶段因缺少连接变量而失败 | 订单 PostgreSQL 专项 4 项通过 |

未设置 PostgreSQL 或 ERP 验收变量时，`dotnet test --solution OpenERP.slnx` 通过 43 项，跳过 2 项真实 ERP 验收。前端 19 项、类型检查和构建通过。
