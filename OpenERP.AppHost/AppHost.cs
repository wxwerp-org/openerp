using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
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
    api.WithReferenceEnvironment(ReferenceEnvironmentInjectionFlags.ConnectionString);
else
    api.WithReference(database);

api.WithReference(redis)
    .WithReference(storage)
    .WaitFor(database)
    .WaitFor(redis)
    .WaitFor(storage);

if (publishing)
{
    api.WithHttpEndpoint(targetPort: 8080);
}
else
{
    api.WithHttpsEndpoint()
        .WithHttpHealthCheck("/health", endpointName: "https");
}

var web = builder.AddViteApp("web", "../OpenERP.Web")
    // 安装器没有 TTY；允许 pnpm 重建不兼容的 node_modules，同时保持锁文件校验。
    .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"])
    .WaitFor(api)
    .WithExternalHttpEndpoints();

if (publishing)
{
    const string credentialsService = "credentials";
    const string postgresCredentials = "postgres-credentials";
    const string s3Credentials = "s3-credentials";
    const string postgresDirectory = "/run/openerp/postgres";
    const string s3Directory = "/run/openerp/s3";
    const string passfile = postgresDirectory + "/current/pgpass";
    var databaseConnectionVariable = $"ConnectionStrings__{database.Resource.Name}";

    var postgresEndpoint = postgres.Resource.PrimaryEndpoint;
    var storageEndpoint = storage.Resource.PrimaryEndpoint;
    // API 和迁移引用同一连接资源，由 Npgsql 从 PGPASSFILE 读取密码。
    var composeDatabase = builder.AddConnectionString("openerpdb", ReferenceExpression.Create(
        $"Host={postgresEndpoint.Property(EndpointProperty.Host)};Port={postgresEndpoint.Property(EndpointProperty.Port)};Database={database.Resource.DatabaseName};Username={postgres.Resource.UserNameReference}"));
    // Compose 不执行 AppHost 的建库回调，首次初始化使用上游 PostgreSQL 镜像的入口。
    postgres.WithVolume(postgresCredentials, postgresDirectory, isReadOnly: true)
        .WithEnvironment("POSTGRES_DB", database.Resource.DatabaseName)
        .WithEnvironment("POSTGRES_PASSWORD_FILE", postgresDirectory + "/current/password")
        .WithEnvironment(context => context.EnvironmentVariables.Remove("POSTGRES_PASSWORD"));

    storage.WithVolume(s3Credentials, s3Directory, isReadOnly: true)
        .WithEnvironment(context =>
        {
            context.EnvironmentVariables.Remove("S3_ACCESS_KEY");
            context.EnvironmentVariables.Remove("S3_SECRET_KEY");
        })
        // 使用 weed server 原有卷上限和稳定的服务名，支持小盘首启和容器重建。
        .WithArgs(context => context.Args.Clear())
        .WithArgs("server", "-volume.max=8", "-master.volumeSizeLimitMB=30720", "-filer", "-s3",
            ReferenceExpression.Create($"-ip={storageEndpoint.Property(EndpointProperty.Host)}"),
            "-ip.bind=0.0.0.0", "-s3.config=" + s3Directory + "/current/s3.json")
        // 由镜像入口降权，S3 配置由一次性服务写入卷。
        .PublishAsDockerComposeService((_, service) => service.Entrypoint.Clear());

    api.WithReference(composeDatabase, connectionName: database.Resource.Name)
        .WithEnvironment("PGPASSFILE", passfile)
        .WithEnvironment("ConnectionStrings__seaweedfs", ReferenceExpression.Create($"Endpoint={storageEndpoint}"))
        .WithVolume(s3Credentials, s3Directory, env: "OPENERP_S3_CREDENTIALS_DIRECTORY", isReadOnly: true);

    // YARP 读取外层 HTTPS 反代写入的原始协议。
    web.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");

    var composeEnvironment = builder.AddDockerComposeEnvironment("compose")
        .WithDashboard(false)
        .ConfigureComposeFile(compose =>
        {
            compose.Name = "openerp";
            var services = compose.Services;
            var apiService = services[api.Resource.Name];
            var initializer = new Service
            {
                Name = credentialsService,
                Image = apiService.Image,
                Command = ["initialize-compose-credentials", postgresDirectory, s3Directory],
                User = "0",
                NetworkMode = "none"
            };
            initializer.Environment[databaseConnectionVariable] = apiService.Environment[databaseConnectionVariable];
            initializer.AddVolume(CredentialMount(postgresCredentials, postgresDirectory, readOnly: false));
            initializer.AddVolume(CredentialMount(s3Credentials, s3Directory, readOnly: false));
            compose.AddService(initializer);

            foreach (var name in new[] { "postgres", "seaweedfs" })
            {
                var service = services[name];
                service.DependsOn ??= [];
                service.DependsOn[credentialsService] = new() { Condition = "service_completed_successfully" };
            }

            apiService.AddVolume(CredentialMount(postgresCredentials, postgresDirectory));

            // Aspire 13.6.5 把 WaitFor 生成成 service_started。由 Compose 等待容器内的就绪探针。
            services["postgres"].Healthcheck = ReadinessProbe("CMD", "pg_isready", "-h", "127.0.0.1");
            services["redis"].Healthcheck = ReadinessProbe("CMD-SHELL", "redis-cli ping | grep -qx PONG");
            // S3 监听早于主节点接受卷注册，重建后需等原有对象的卷可访问。
            services["seaweedfs"].Healthcheck = ReadinessProbe("CMD-SHELL",
                "curl -fsS -o /dev/null http://127.0.0.1:8333/readyz && " +
                "curl -fsS http://127.0.0.1:9333/dir/status | grep -Eq '\"DataNodes\"[[:space:]]*:[[:space:]]*\\[[[:space:]]*\\{'");
            services["migrations"].DependsOn["postgres"].Condition = "service_healthy";
            services["api"].DependsOn["redis"].Condition = "service_healthy";
            services["api"].DependsOn["seaweedfs"].Condition = "service_healthy";

            foreach (var name in new[] { "postgres", "redis", "seaweedfs", "api", "web" })
                services[name].Restart = "unless-stopped";

            // 外部 HTTPS 反代连接宿主机回环地址。目标端口沿用生成的 YARP 监听端口。
            var ports = services["web"].Ports;
            ports[0] = $"127.0.0.1:${{WEB_PORT:-8080}}:{ports.Single()}";
        });

    var migrations = api.AddEFMigrations("migrations")
        .WithReference(composeDatabase)
        .WaitFor(database)
        // bundle 以 Web API 为启动项目，依赖 Microsoft.AspNetCore.App。
        .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
        .WithVolume(postgresCredentials, postgresDirectory, isReadOnly: true)
        .WithEnvironment("PGPASSFILE", passfile)
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
                composeEnvironment.ConfigureComposeFile(compose =>
                {
                    compose.Services[resource.Name].Image = image;
                    if (resource == api.Resource)
                        compose.Services[credentialsService].Image = image;
                });
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

static Volume CredentialMount(string name, string path, bool readOnly = true) => new()
{
    Name = name,
    Type = "volume",
    Source = name,
    Target = path,
    ReadOnly = readOnly
};

static Healthcheck ReadinessProbe(params string[] test) => new()
{
    Test = [.. test],
    Interval = "3s",
    Timeout = "2s",
    Retries = 20,
    StartPeriod = "10s"
};
