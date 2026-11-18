using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var publishing = builder.ExecutionContext.IsPublishMode;
var compose = builder.AddDockerComposeEnvironment("compose").WithDashboard(false);

// 开发卷由 Aspire 按 AppHost 路径隔离；部署卷名固定，升级时复用原数据。
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithDataVolume(name: publishing ? "postgres-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("openerp");

var redis = builder.AddRedis("redis")
    .WithImageTag("8-alpine")
    .WithDataVolume(name: publishing ? "redis-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);
// 部署包使用 Compose 内网 Redis；本地沿用 Aspire 生成的开发密码。
if (publishing)
    redis.WithPassword(null);

var storage = builder.AddSeaweedFS("seaweedfs")
    .WithImageTag("4.53")
    .WithS3()
    .WithDataVolume(name: publishing ? "seaweedfs-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);

// Master/Volume 就绪后，还需确认 API 启动所依赖的 S3 列桶操作可用。
builder.Services.AddHealthChecks()
    .AddCheck("seaweedfs-s3", new SeaweedFsS3HealthCheck(storage.Resource), timeout: TimeSpan.FromSeconds(10));
storage.WithHealthCheck("seaweedfs-s3");

var api = builder.AddProject<Projects.OpenERP_Api>("api");
if (publishing)
{
    // 发布连接从凭据文件取密。WithReference 在注册时读取注入策略，须先于下面的引用。
    api.WithReferenceEnvironment(ReferenceEnvironmentInjectionFlags.ConnectionString)
        .WithHttpEndpoint(targetPort: 8080);
}
else
{
    // 本地开发由 Aspire 提供数据库连接和 HTTPS；部署的 TLS 在外层反向代理终止。
    api.WithReference(database)
        .WithHttpsEndpoint()
        .WithHttpHealthCheck("/health", endpointName: "https");
}

api.WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

// AddViteApp 发布时采用项目 Dockerfile；开发与 Caddy 均通过资源引用发现 API。
var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

if (publishing)
    ComposeDeployment.Configure(compose, postgres, database, redis, storage, api, web);

if (builder.ExecutionContext.IsRunMode)
{
    // 开发证书和浏览器日志仅服务于本地 Vite 会话。
#pragma warning disable ASPIRECERTIFICATES001 // 使用 Aspire 的本地 HTTPS 证书集成。
    web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // 使用已启用的开发浏览器诊断。
    web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001
}

builder.Build().Run();
