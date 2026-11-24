using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
if (builder.ExecutionContext.IsPublishMode)
{
    await ComposeDeployment.RunAsync(builder);
    return;
}

var api = builder.AddDotnetProject("api", "../OpenERP.Api/OpenERP.Api.csproj")
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health", endpointName: "https");

var mode = builder.Configuration["OpenERP:Mode"];
if (string.Equals(mode, "Native", StringComparison.OrdinalIgnoreCase))
{
    var directory = NativeDataDirectory.Resolve(builder.Configuration["OpenERP:DataDirectory"], builder.AppHostDirectory);
    api.WithEnvironment("Native__DataDirectory", directory);
}
else if (string.IsNullOrEmpty(mode) || string.Equals(mode, "Docker", StringComparison.OrdinalIgnoreCase))
{
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

    api.WithReference(database)
        .WithReference(redis)
        .WithReference(storage)
        .WaitFor(database)
        .WaitFor(redis)
        .WaitFor(storage);
}
else
{
    throw new InvalidOperationException("OpenERP:Mode 应为 Native 或 Docker。");
}

var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

// 开发证书和浏览器日志仅服务于本地 Vite 会话。
#pragma warning disable ASPIRECERTIFICATES001 // 使用 Aspire 的本地 HTTPS 证书集成。
web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // 使用已启用的开发浏览器诊断。
web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
