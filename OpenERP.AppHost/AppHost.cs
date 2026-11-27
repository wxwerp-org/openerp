using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
if (builder.ExecutionContext.IsPublishMode)
{
    await ComposeDeployment.RunAsync(builder);
    return;
}

builder.AddDockerComposeEnvironment("compose").WithDashboard(false);

// 开发卷由 Aspire 按 AppHost 路径隔离。
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("openerp");

var redis = builder.AddRedis("redis")
    .WithImageTag("8-alpine")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var storage = builder.AddSeaweedFS("seaweedfs")
    .WithImageTag("4.53")
    .WithS3()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Master/Volume 就绪后，还需确认 API 启动所依赖的 S3 列桶操作可用。
builder.Services.AddHealthChecks()
    .AddCheck("seaweedfs-s3", new SeaweedFsS3HealthCheck(storage.Resource), timeout: TimeSpan.FromSeconds(10));
storage.WithHealthCheck("seaweedfs-s3");

var api = builder.AddDotnetProject("api", "../OpenERP.Api/OpenERP.Api.csproj")
    .WithReference(database)
    // 固定宿主端口，重启后地址不变。
    .WithHttpsEndpoint(port: DevPorts.Api)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();
// 固定前端的宿主端口：每次启动都用同一个地址访问。
web.WithEndpoint("http", endpoint => endpoint.Port = DevPorts.Web);

// 开发证书和浏览器日志仅服务于本地 Vite 会话。
#pragma warning disable ASPIRECERTIFICATES001 // 使用 Aspire 的本地 HTTPS 证书集成。
web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // 使用已启用的开发浏览器诊断。
web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();

/// <summary>本地开发固定使用的宿主端口，避免每次启动地址都变。</summary>
static class DevPorts
{
    internal const int Web = 5173;
    internal const int Api = 5201;
}
