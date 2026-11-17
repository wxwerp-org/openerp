using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var e2e = builder.Configuration["E2E:Mode"] is "stub" or "live";

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine");
var database = postgres.AddDatabase("openerp");

var redis = builder.AddRedis("redis")
    .WithImageTag("8-alpine");

var storage = builder.AddSeaweedFS("seaweedfs")
    .WithImageTag("4.53")
    .WithS3();

// E2E 使用默认 session 生命周期和临时容器文件系统，不访问开发数据卷。
if (!e2e)
{
    postgres.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
    redis.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
    storage.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
}

// Master/Volume 就绪后，还需确认 API 启动所依赖的 S3 列桶操作可用。
builder.Services.AddHealthChecks()
    .AddCheck("seaweedfs-s3", new SeaweedFsS3HealthCheck(storage.Resource), timeout: TimeSpan.FromSeconds(10));
storage.WithHealthCheck("seaweedfs-s3");

var api = builder.AddProject<Projects.OpenERP_Api>("api")
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(database)
    .WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

// Token 仅注入后端；未配置时仍可启动并在店铺页提示。
if (e2e)
{
    api.WithEnvironment("Erp__AccessToken", builder.AddParameter("wxw-erp-access-token", secret: true))
        .WithEnvironment("Erp__BaseUrl", builder.Configuration["E2E:WxwUrl"] ?? "");
}
else if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:wxw-erp-access-token"]))
{
    var erpToken = builder.AddParameter("wxw-erp-access-token", secret: true);
    api.WithEnvironment("Erp__AccessToken", erpToken);
}

var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WithReference(api)
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("https"))
    .WaitFor(api)
    .WithExternalHttpEndpoints();

#pragma warning disable ASPIRECERTIFICATES001 // Use Aspire's local HTTPS certificate integration for Vite.
web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // Explicitly approved development-only browser diagnostics.
web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
