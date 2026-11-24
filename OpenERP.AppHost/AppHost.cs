using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
if (builder.ExecutionContext.IsPublishMode)
{
    await ComposeDeployment.RunAsync(builder);
    return;
}

builder.AddDockerComposeEnvironment("compose").WithDashboard(false);

var api = builder.AddDotnetProject("api", "../OpenERP.Api/OpenERP.Api.csproj")
    .WithHttpsEndpoint()
    .WithHttpHealthCheck("/health", endpointName: "https");

switch (builder.Configuration["Database:Provider"]?.ToUpperInvariant())
{
    case null or "POSTGRESQL":
        // 开发卷由 Aspire 按 AppHost 路径隔离。
        var database = builder.AddPostgres("postgres")
            .WithImageTag("18-alpine")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent)
            .AddDatabase("openerp");
        api.WithEnvironment("Database__Provider", "PostgreSQL").WithReference(database).WaitFor(database);
        break;
    case "SQLITE":
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("openerp")))
            throw new InvalidOperationException("SQLite 模式需要配置 ConnectionStrings:openerp。");
        api.WithEnvironment("Database__Provider", "Sqlite").WithReference(builder.AddConnectionString("openerp"));
        break;
    default:
        throw new InvalidOperationException("Database:Provider 必须为 PostgreSQL 或 Sqlite。");
}

switch (builder.Configuration["Cache:Provider"]?.ToUpperInvariant())
{
    case null or "REDIS":
        var redis = builder.AddRedis("redis")
            .WithImageTag("8-alpine")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
        api.WithReference(redis).WaitFor(redis);
        break;
    case "MEMORY":
        api.WithEnvironment("ConnectionStrings__redis", "");
        break;
    default:
        throw new InvalidOperationException("Cache:Provider 必须为 Redis 或 Memory。");
}

switch (builder.Configuration["ObjectStorage:Provider"]?.ToUpperInvariant())
{
    case null or "S3":
        var storage = builder.AddSeaweedFS("seaweedfs")
            .WithImageTag("4.53")
            .WithS3()
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);

        // Master/Volume 就绪后，还需确认 API 启动所依赖的 S3 列桶操作可用。
        builder.Services.AddHealthChecks()
            .AddCheck("seaweedfs-s3", new SeaweedFsS3HealthCheck(storage.Resource), timeout: TimeSpan.FromSeconds(10));
        storage.WithHealthCheck("seaweedfs-s3");
        api.WithEnvironment("ObjectStorage__Provider", "S3").WithReference(storage).WaitFor(storage);
        break;
    case "LOCAL":
        var rootPath = builder.Configuration["ObjectStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException("本机对象存储需要配置 ObjectStorage:RootPath。");
        api.WithEnvironment("ObjectStorage__Provider", "Local").WithEnvironment("ObjectStorage__RootPath", rootPath);
        break;
    default:
        throw new InvalidOperationException("ObjectStorage:Provider 必须为 S3 或 Local。");
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
