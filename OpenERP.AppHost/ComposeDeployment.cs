using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.EntityFrameworkCore;
using Aspire.Hosting.JavaScript;

namespace OpenERP.AppHost;

internal static class ComposeDeployment
{
    private const string Credentials = "credentials";
    private const string PostgresCredentials = "postgres-credentials";
    private const string S3Credentials = "s3-credentials";
    private const string PostgresDirectory = "/run/openerp/postgres";
    private const string S3Directory = "/run/openerp/s3";
    private const string Passfile = PostgresDirectory + "/current/pgpass";

    internal static void Configure(
        IResourceBuilder<DockerComposeEnvironmentResource> compose,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<RedisResource> redis,
        IResourceBuilder<SeaweedFSContainerResource> storage,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<ViteAppResource> web)
    {
        var builder = compose.ApplicationBuilder;
        var postgresEndpoint = postgres.Resource.PrimaryEndpoint;
        // API 和迁移共享无密码的连接串，由 Npgsql 通过 PGPASSFILE 读取密码。
        var composeDatabase = builder.AddConnectionString("openerpdb", ReferenceExpression.Create(
            $"Host={postgresEndpoint.Property(EndpointProperty.Host)};Port={postgresEndpoint.Property(EndpointProperty.Port)};Database={database.Resource.DatabaseName};Username={postgres.Resource.UserNameReference}"));

        // Compose 不执行 AppHost 的建库回调，首次建库交给 PostgreSQL 镜像入口。
        postgres.WithVolume(PostgresCredentials, PostgresDirectory, isReadOnly: true)
            .WithEnvironment("POSTGRES_DB", database.Resource.DatabaseName)
            .WithEnvironment("POSTGRES_PASSWORD_FILE", PostgresDirectory + "/current/password")
            .WithEnvironment(context => context.EnvironmentVariables.Remove("POSTGRES_PASSWORD"))
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Healthcheck = ReadinessProbe("CMD", "pg_isready", "-h", "127.0.0.1");
                service.Restart = "unless-stopped";
            });
        redis.PublishAsDockerComposeService((_, service) =>
        {
            service.Healthcheck = ReadinessProbe("CMD-SHELL", "redis-cli ping | grep -qx PONG");
            service.Restart = "unless-stopped";
        });

        ConfigureStorage(storage);
        api.WithReference(composeDatabase, connectionName: database.Resource.Name)
            .WithEnvironment("PGPASSFILE", Passfile)
            .WithEnvironment("ConnectionStrings__seaweedfs", ReferenceExpression.Create($"Endpoint={storage.Resource.PrimaryEndpoint}"))
            .WithVolume(S3Credentials, S3Directory, env: "OPENERP_S3_CREDENTIALS_DIRECTORY", isReadOnly: true)
            .PublishAsDockerComposeService((_, service) =>
            {
                service.AddVolume(CredentialMount(PostgresCredentials, PostgresDirectory, readOnly: true));
                // Aspire 13.6.5 的 WaitFor 只生成 service_started；升级后复查是否原生导出就绪依赖。
                service.DependsOn[redis.Resource.Name].Condition = "service_healthy";
                service.DependsOn[storage.Resource.Name].Condition = "service_healthy";
                service.Restart = "unless-stopped";
            });

        var migrations = api.AddEFMigrations("migrations")
            .WithReference(composeDatabase)
            .WaitFor(database)
            // bundle 以 Web API 为启动项目，运行时需要 Microsoft.AspNetCore.App。
            .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:10.0")
            .WithVolume(PostgresCredentials, PostgresDirectory, isReadOnly: true)
            .WithEnvironment("PGPASSFILE", Passfile)
            .PublishAsDockerComposeService((_, service) =>
            {
                service.DependsOn[postgres.Resource.Name].Condition = "service_healthy";
                service.Restart = "no";
            });
        api.WaitForCompletion(migrations);

        web.PublishAsDockerComposeService((_, service) =>
        {
            // 外层反代负责 TLS，对宿主机只开放回环地址；容器端口来自 Aspire。
            var port = service.Ports.Single();
            service.Ports[0] = $"127.0.0.1:${{WEB_PORT:-8080}}:{port}";
            service.Restart = "unless-stopped";
        });

        compose.ConfigureComposeFile(file =>
        {
            file.Name = "openerp";
            var connectionVariable = $"ConnectionStrings__{database.Resource.Name}";
            AddCredentialsService(file, file.Services[api.Resource.Name], connectionVariable);
            foreach (var resource in new IResource[] { postgres.Resource, storage.Resource })
            {
                var service = file.Services[resource.Name];
                service.DependsOn ??= [];
                service.DependsOn[Credentials] = new() { Condition = "service_completed_successfully" };
            }
        });

        if (builder.Configuration["ReleaseTag"] is { } releaseTag)
            ConfigureReleaseImages(compose, api, web, migrations, releaseTag);
    }

    private static void ConfigureStorage(IResourceBuilder<SeaweedFSContainerResource> storage)
    {
        storage.WithVolume(S3Credentials, S3Directory, isReadOnly: true)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables.Remove("S3_ACCESS_KEY");
                context.EnvironmentVariables.Remove("S3_SECRET_KEY");
            })
            // 沿用镜像的卷上限，使用稳定的服务名和凭据文件，支持小盘首启与容器重建。
            .WithArgs(context => context.Args.Clear())
            .WithArgs("server", "-volume.max=8", "-master.volumeSizeLimitMB=30720", "-filer", "-s3",
                ReferenceExpression.Create($"-ip={storage.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}"),
                "-ip.bind=0.0.0.0", "-s3.config=" + S3Directory + "/current/s3.json")
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Entrypoint.Clear();
                // S3 开始监听早于数据卷注册，重建后须等已有对象可访问。
                service.Healthcheck = ReadinessProbe("CMD-SHELL",
                    "curl -fsS -o /dev/null http://127.0.0.1:8333/readyz && " +
                    "curl -fsS http://127.0.0.1:9333/dir/status | grep -Eq '\"DataNodes\"[[:space:]]*:[[:space:]]*\\[[[:space:]]*\\{'");
                service.Restart = "unless-stopped";
            });
    }

    private static void AddCredentialsService(ComposeFile file, Service api, string connectionVariable)
    {
        var initializer = new Service
        {
            Name = Credentials,
            Image = api.Image,
            Command = ["initialize-compose-credentials", PostgresDirectory, S3Directory],
            User = "0",
            NetworkMode = "none"
        };
        initializer.Environment[connectionVariable] = api.Environment[connectionVariable];
        initializer.AddVolume(CredentialMount(PostgresCredentials, PostgresDirectory, readOnly: false));
        initializer.AddVolume(CredentialMount(S3Credentials, S3Directory, readOnly: false));
        file.AddService(initializer);
    }

    private static void ConfigureReleaseImages(
        IResourceBuilder<DockerComposeEnvironmentResource> compose,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<ViteAppResource> web,
        IResourceBuilder<EFMigrationResource> migrations,
        string releaseTag)
    {
        var builder = compose.ApplicationBuilder;
#pragma warning disable ASPIRECOMPUTE003 // 使用 Aspire 官方容器镜像推送流水线。
        var registryEndpoint = builder.AddParameter("registry-endpoint");
        var registryRepository = builder.AddParameter("registry-repository");
        builder.AddContainerRegistry("registry", registryEndpoint, registryRepository);
#pragma warning restore ASPIRECOMPUTE003

#pragma warning disable ASPIREPIPELINES003 // 让官方推送流水线将自有镜像绑定到 GitHub Release 标签。
        api.WithRemoteImageTag(releaseTag);
        web.WithRemoteImageTag(releaseTag);
        migrations.WithRemoteImageTag(releaseTag);
        // 普通发布保留镜像占位符；Release 附件须在没有 .env 的情况下直接使用已推送的镜像。
        builder.Eventing.Subscribe<BeforeStartEvent>(async (_, ct) =>
        {
            foreach (var resource in new IResource[] { api.Resource, web.Resource, migrations.Resource })
            {
                var image = await ((IValueProvider)new ContainerImageReference(resource)).GetValueAsync(ct);
                compose.ConfigureComposeFile(file =>
                {
                    file.Services[resource.Name].Image = image;
                    if (resource == api.Resource)
                        file.Services[Credentials].Image = image;
                });
                compose.ConfigureEnvFile(env => env.Remove($"{resource.Name.ToUpperInvariant()}_IMAGE"));
            }
        });
#pragma warning restore ASPIREPIPELINES003
    }

    private static Volume CredentialMount(string name, string path, bool readOnly) => new()
    {
        Name = name,
        Type = "volume",
        Source = name,
        Target = path,
        ReadOnly = readOnly
    };

    private static Healthcheck ReadinessProbe(params string[] test) => new()
    {
        Test = [.. test],
        Interval = "3s",
        Timeout = "2s",
        Retries = 20,
        StartPeriod = "10s"
    };
}
