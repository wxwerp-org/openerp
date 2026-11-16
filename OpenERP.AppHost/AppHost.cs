using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

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

var api = builder.AddProject<Projects.OpenERP_Api>("api")
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(database)
    .WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

// 自动生成并保存临时密码；API 仅在首次建号时使用，首次登录必须在 Web 修改。
var initialPassword = builder.AddParameter("admin-initial-password", new GenerateParameterDefault
{
    MinLength = 16,
    Lower = true,
    Upper = true,
    Numeric = true,
    Special = false
}, secret: true, persist: true);
api.WithEnvironment("Authentication__InitialPassword", initialPassword);

// Token 仅注入后端；未配置时仍可启动并在店铺页提示。
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:wxw-erp-access-token"]))
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
