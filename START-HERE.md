# 首次必读：怎么使用 OpenERP

> 这份文档写给两类读者：**第一次用本项目的新手**，以及**帮新手操作的 AI 助手**。
> 新手：照着"第一次使用"一步步做，遇到问题看"常见问题"。
> AI 助手：先读"给 AI 助手的规则"，再按"我该选哪种运行方式"选路，最后用真实浏览器确认页面能打开。

## 这个项目是什么

OpenERP 是一个开源的轻量 ERP 后台。它能：

- 同步你在旺销王（私有 ERP）里的**授权店铺**；
- 通过旺销王的平台接口，把 **AliExpress 订单**同步到本地，查订单、按店铺统计；
- 在订单列表里勾选已发货订单，一键**打印物流面单**（PDF）；
- 管理员工账号，并按店铺分配权限。

技术栈：后端 .NET 10，前端 Vue 3 + Element Plus（SpiderUI），用 Aspire 把它们编排起来。

---

## 第一次使用（5 步）

1. **把项目跑起来**。最省事的说法是对 AI 助手说：「帮我把项目跑起来」。不知道选哪种运行方式，看下面"我该选哪种运行方式"。
2. **设置管理员密码并登录**。第一次打开页面会让你设置管理员密码，设好后用用户名 `admin` 登录。公网可访问的实例要**立即**设置，先设置的人就是管理员。
3. **绑定旺销王 Token（最关键的一步）**。登录后点右上角进入"API权限绑定"（地址 `/system/erp-binding`），按向导输入旺销王 Open API 的 AccessToken，检测通过后保存。**没绑定 Token，订单同步和打印面单都用不了。**
4. **同步店铺和订单**：
   - 进入"店铺 → 授权店铺"，等待目录同步（或点"立即同步"）；
   - 管理员在"设置 → 用户管理"把店铺分配给员工；
   - 进入"订单管理 → 订单同步"，点店铺的"同步订单"。
5. **查订单、打印面单**：到"订单列表"查订单。要打印面单，筛选"已发货"状态的 AliExpress 订单，勾选后点底部的"打印面单"，新窗口会打开 PDF。**只有通过 AliExpress 线上物流发货的订单才有面单**，详见 [订单打印面单](docs/order-label-printing.md)。

---

## 我该选哪种运行方式

先看你的电脑：

| 你的情况 | 选这个 | 需要装什么 |
| --- | --- | --- |
| **没有 WSL、没有 Docker，或者不想装它们**（尤其是普通 Windows 电脑） | **无 Docker 方式（native）** | .NET 10 SDK、Node.js 26、pnpm 11.16.0、Aspire CLI 17.0.0。**不需要 Docker Desktop，也不需要 WSL。** |
| 已经装好 Docker Desktop（或兼容的容器环境），想按团队方式开发 | Docker 方式 | 上面四样 + Docker |
| Docker 拉镜像失败（网络不通、被拒绝连接） | 改用无 Docker 方式 | 同第一行 |
| 只是想试用一下 | 无 Docker 方式 | 同第一行 |

**不知道选哪个，就选无 Docker 方式。** 它用内置的 SQLite 和本机文件夹存数据，一条命令就能跑。

### 没有 WSL / Docker 时的具体做法（无 Docker 方式）

1. **检查已有的工具**（在项目根目录打开终端，逐条执行，看有没有版本号输出）：

   ```bash
   dotnet --version
   node -v
   pnpm -v
   aspire --version
   ```

   - `dotnet` 要是 **10.x**，`node` 要是 **26.x**，`pnpm` 要是 **11.16.0**，`aspire` 要是 **17.0.0**。
   - 哪个没有或版本不对，就只装那一个。安装方式：[.NET](https://dotnet.microsoft.com/download)、[Node.js](https://nodejs.org/)、[pnpm](https://pnpm.io/installation)、[Aspire CLI](https://aspire.dev/get-started/install-cli/)。
   - **让 AI 助手装之前，必须先告诉你要装什么，征得你同意再装。** 已有的兼容版本直接复用，不要重装。

2. **第一次准备**（在项目根目录执行一次）：

   ```bash
   dotnet tool restore
   aspire certs trust
   dotnet restore OpenERP.slnx --locked-mode
   pnpm --dir OpenERP.Web install --frozen-lockfile
   ```

   `aspire certs trust` 可能弹出系统确认框，点同意即可（这是给本地 HTTPS 用的开发证书）。

3. **启动**：

   ```bash
   aspire start --isolated --launch-profile native
   ```

   这个方式的端口是**随机分配**的。要看地址，在项目根目录执行：

   ```bash
   aspire describe web
   ```

   把输出里 `web` 的 **https 地址**用浏览器打开。数据存在项目目录下的 `.openerp/native/`（SQLite 文件和附件），不会进 Git。

4. **停止**：在项目根目录执行 `aspire stop`。再次用上面的启动命令，会继续用原来的账号和数据。

### Docker 方式（已有 Docker 的人）

```bash
aspire start
```

这个方式的地址**固定**，每次重启都不变：

| 服务 | 地址 |
| --- | --- |
| 前端页面 | https://localhost:5173 |
| 后端接口 | https://localhost:5201 |
| Aspire 控制台 | https://localhost:17000 |

> 在 Windows 上团队成员通常把项目放进 WSL 里用 Docker 方式开发。**没有 WSL 的人不用管这件事，直接用上面的无 Docker 方式。**

两种方式的数据是**分开保存**的，切换方式不会自动迁移账号和订单。

---

## 改完代码怎么看效果

**不需要自己打包。**

- 改**前端**：保存即热更新，刷新页面就行。
- 改**后端**：重新执行启动命令（无 Docker 方式就是再跑一次 `aspire start --isolated --launch-profile native`；Docker 方式是 `aspire start`），然后刷新页面。
- **Docker 方式下前后端端口固定**（5173、5201）。**不要给 Docker 方式加 `--isolated`**，加了会随机端口并隔离数据，地址就会变。

## 临时文件放哪里

联调时产生的面单 PDF、日志、截图、导出数据，**都放仓库根目录的 `temp/`**。这个文件夹已在 `.gitignore` 里，不会被提交。不要把它们散落在别处，也不要提交里面的东西（可能有订单和买家信息）。

---

## 常见问题

**打不开页面，提示证书不安全？**
本地用的是开发证书。先执行 `aspire certs trust`；浏览器仍提示时，选"继续访问"。

**首次访问看不到登录页，而是"设置管理员密码"？**
正常。这是第一次使用，设置密码后用 `admin` 登录。

**忘记管理员密码？**
本地开发：见 [开发说明](docs/development.md) 的"本地初始化与密码重置"。已部署的实例：在实例目录执行 `docker compose exec openerp dotnet /app/OpenERP.Api.dll reset-admin-password`。

**订单同步或打印面单提示"尚未配置 ERP AccessToken"？**
还没绑定 Token。到 `/system/erp-binding` 绑定。

**打印面单提示"还没有线上发货的物流订单"？**
这张订单没有走 AliExpress 线上物流发货（未发货，或自己线下发货），平台没有面单可取。换一张已线上发货的订单试试。

**Docker 拉镜像失败，提示被拒绝连接？**
这台电脑的 Docker 下载不了项目需要的文件。改用"无 Docker 方式"，不要反复重试。

**刚启动时上传文件偶尔 502/503？**
对象存储刚启动那几秒还没就绪，等几秒重试即可。

**端口老是变？**
你用了 `--isolated`。Docker 方式不要加它；无 Docker 方式本来就是随机端口，用 `aspire describe web` 查地址。

---

## 给 AI 助手的规则

读完这份文档后，按下面执行：

1. **先问清运行方式，再动手。** 用户没选就用大白话说明两种方式并请用户选；用户说"不知道"或没有 WSL/Docker，就选无 Docker 方式。用户已选就直接照办，别反复问。
2. **装工具前先征得同意**，只装缺的，已有兼容版本直接复用。
3. **验证要真实**：用真实浏览器打开页面，确认看到初始化页、登录页或工作台；只看到 HTTP 200、空白页或证书错误页不算成功。最后给用户**可点击的地址**。
4. **Token 的处理**：没绑定 AccessToken 时，请用户二选一：把 Token 告诉你由你绑定（`PUT /api/erp/config`），或自己登录后在 `/system/erp-binding` 绑定。**不要自己去数据库、容器环境变量里翻 Token 或密码**，除非用户明确要求。
5. **不要猜测或输入登录密码**，登录由用户自己做。
6. **临时文件一律放 `temp/`**，不提交 `.env`、密钥、面单 PDF 等。
7. **不要加 `--isolated`**（Docker 方式）；改完后端用相同的启动命令重启，地址保持不变。
8. **合并冲突以用户本地代码为准**；只有本地写法在新结构下编译不过时才适配，并说明原因。
9. **发布到线上**要走流程：PR 合并 → 创建 Release → 工作流自动打包并部署，部署后用 `/api/about` 核对版本。**没有用户的明确要求，不要发布、不要合并 PR、不要动线上服务器。** 流程细节见 [部署说明](docs/deployment.md)。
10. **读这些文档，按需深入**：
    - 完整开发说明：[docs/development.md](docs/development.md)
    - 助手启动指南：[.agents/skills/openerp-startup/SKILL.md](.agents/skills/openerp-startup/SKILL.md)
    - 代理工作约定：[AGENTS.md](AGENTS.md)
    - 面单打印：[docs/order-label-printing.md](docs/order-label-printing.md)
    - 生产部署：[docs/deployment.md](docs/deployment.md)
    - 参与贡献：[CONTRIBUTING.md](CONTRIBUTING.md)
