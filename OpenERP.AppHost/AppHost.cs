using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Microsoft.Extensions.DependencyInjection;
using OpenERP.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var publishing = builder.ExecutionContext.IsPublishMode;
var releaseTag = builder.Configuration["ReleaseTag"];

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithDataVolume(name: publishing ? "postgres-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("openerp");

var redis = builder.AddRedis("redis")
    .WithImageTag("8-alpine")
    .WithDataVolume(name: publishing ? "redis-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);

var storage = builder.AddSeaweedFS("seaweedfs")
    .WithImageTag("4.53")
    .WithS3()
    .WithDataVolume(name: publishing ? "seaweedfs-data" : null)
    .WithLifetime(ContainerLifetime.Persistent);

// Master/Volume 就绪后，还需确认 API 启动所依赖的 S3 列桶操作可用。
builder.Services.AddHealthChecks()
    .AddCheck("seaweedfs-s3", new SeaweedFsS3HealthCheck(storage.Resource), timeout: TimeSpan.FromSeconds(10));
storage.WithHealthCheck("seaweedfs-s3");

var api = builder.AddProject<Projects.OpenERP_Api>("api")
    .WithReference(database)
    .WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

if (publishing)
{
    var signingKey = builder.AddParameter("jwt-signing-private-key-pem", secret: true);
    api.WithHttpEndpoint(targetPort: 8080)
        .WithEnvironment("Authentication__SigningPrivateKeyPem", signingKey);
}
else
{
    api.WithHttpsEndpoint()
        .WithHttpHealthCheck("/health", endpointName: "https");
}

// Token 仅注入后端；未配置时仍可启动并在店铺页提示。
if (publishing || !string.IsNullOrWhiteSpace(builder.Configuration["Parameters:wxw-erp-access-token"]))
{
    var erpToken = builder.AddParameter("wxw-erp-access-token", secret: true);
    api.WithEnvironment("Erp__AccessToken", erpToken);
}

var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WaitFor(api)
    .WithExternalHttpEndpoints();

if (publishing)
{
    // Compose 不执行 AppHost 的建库回调，首次初始化使用上游 PostgreSQL 镜像的入口。
    postgres.WithEnvironment("POSTGRES_DB", database.Resource.DatabaseName);

    var composeEnvironment = builder.AddDockerComposeEnvironment("compose")
        .WithDashboard(false)
        .ConfigureComposeFile(compose =>
        {
            // Aspire 13.6.5 把 WaitFor 生成成 service_started。由 Compose 等待容器内的就绪探针。
            var services = compose.Services;
            services["postgres"].Healthcheck = ReadinessProbe("CMD", "pg_isready", "-h", "127.0.0.1");
            services["redis"].Healthcheck = ReadinessProbe("CMD-SHELL", "REDISCLI_AUTH=\"$$REDIS_PASSWORD\" redis-cli ping | grep -qx PONG");
            services["seaweedfs"].Healthcheck = ReadinessProbe("CMD", "curl", "-fsS", "http://127.0.0.1:8333/readyz");
            services["migrations"].DependsOn["postgres"].Condition = "service_healthy";
            services["api"].DependsOn["redis"].Condition = "service_healthy";
            services["api"].DependsOn["seaweedfs"].Condition = "service_healthy";

            // Compose 在启动迁移或应用前拒绝缺少必填运行配置的部署。
            services["postgres"].Environment["POSTGRES_PASSWORD"] = "${POSTGRES_PASSWORD:?请配置 POSTGRES_PASSWORD}";
            services["redis"].Environment["REDIS_PASSWORD"] = "${REDIS_PASSWORD:?请配置 REDIS_PASSWORD}";
            services["seaweedfs"].Environment["S3_ACCESS_KEY"] = "${SEAWEEDFS_ACCESSKEY:?请配置 SEAWEEDFS_ACCESSKEY}";
            services["seaweedfs"].Environment["S3_SECRET_KEY"] = "${SEAWEEDFS_SECRETKEY:?请配置 SEAWEEDFS_SECRETKEY}";
            services["api"].Environment["Authentication__SigningPrivateKeyPem"] =
                "${JWT_SIGNING_PRIVATE_KEY_PEM:?请配置 JWT_SIGNING_PRIVATE_KEY_PEM}";

            // 外部 HTTPS 反代连接宿主机回环地址。目标端口沿用生成的 YARP 监听端口。
            var ports = services["web"].Ports;
            ports[0] = $"127.0.0.1:${{WEB_PORT:-8080}}:{ports.Single()}";
        });

    var migrations = api.AddEFMigrations("migrations")
        .WithReference(database)
        .WaitFor(database)
        // bundle 以 Web API 为启动项目，依赖 Microsoft.AspNetCore.App。
        .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
        .PublishAsDockerComposeService((_, service) => service.Restart = "no");
    api.WaitForCompletion(migrations);

#pragma warning disable ASPIREJAVASCRIPT001 // 使用 Aspire 生成的 YARP 容器交付 Vite 静态产物。
    web.PublishAsStaticWebsite(apiPath: "/api", apiTarget: api);
#pragma warning restore ASPIREJAVASCRIPT001

    if (releaseTag is not null)
    {
#pragma warning disable ASPIRECOMPUTE003 // 使用 Aspire 官方容器镜像推送流水线。
        var registryEndpoint = builder.AddParameter("registry-endpoint");
        var registryRepository = builder.AddParameter("registry-repository");
        builder.AddContainerRegistry("registry", registryEndpoint, registryRepository);
#pragma warning restore ASPIRECOMPUTE003

#pragma warning disable ASPIREPIPELINES003 // 让官方推送流水线将自有镜像绑定到 GitHub Release 标签。
        api.WithRemoteImageTag(releaseTag);
        web.WithRemoteImageTag(releaseTag);
        migrations.WithRemoteImageTag(releaseTag);
        // 发布模板里的镜像占位符不带值。交付时用 Aspire 推送流水线解析出的完整镜像引用。
        builder.Eventing.Subscribe<BeforeStartEvent>(async (_, ct) =>
        {
            foreach (var resource in new IResource[] { api.Resource, web.Resource, migrations.Resource })
            {
                var image = await ((IValueProvider)new ContainerImageReference(resource)).GetValueAsync(ct);
                composeEnvironment.ConfigureComposeFile(compose => compose.Services[resource.Name].Image = image);
                composeEnvironment.ConfigureEnvFile(env => env.Remove($"{resource.Name.ToUpperInvariant()}_IMAGE"));
            }
        });
#pragma warning restore ASPIREPIPELINES003
    }
}
else
{
    web.WithReference(api)
        .WithEnvironment("API_BASE_URL", api.GetEndpoint("https"));

#pragma warning disable ASPIRECERTIFICATES001 // Use Aspire's local HTTPS certificate integration for Vite.
    web.WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001

#pragma warning disable ASPIREBROWSERLOGS001 // Explicitly approved development-only browser diagnostics.
    web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001
}

builder.Build().Run();

static Healthcheck ReadinessProbe(params string[] test) => new()
{
    Test = [.. test],
    Interval = "3s",
    Timeout = "2s",
    Retries = 20,
    StartPeriod = "10s"
};
