using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.EntityFrameworkCore;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.Logging;

namespace OpenERP.AppHost;

internal static class ComposeDeployment
{
    private const string Image = "openerp";
    private const string LocalTag = "local";
    private const string BaseImageArgument = "OPENERP_BASE_IMAGE";
    private const string BundleStep = "migrations-generate-migration-bundle";

    internal static async Task RunAsync(IDistributedApplicationBuilder builder)
    {
        var runId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(builder.AppHostDirectory, "obj", "single-container-build", runId);
        var deploy = Path.GetFullPath("../deploy", builder.AppHostDirectory);
        var suppliedBaseImage = builder.Configuration[BaseImageArgument];
        var buildBaseLocally = string.IsNullOrEmpty(suppliedBaseImage);
        var baseImage = buildBaseLocally ? $"openerp-base:{runId}" : suppliedBaseImage!;
        var compose = builder.AddDockerComposeEnvironment("compose").WithDashboard(false);

        var api = builder.AddDotnetProject("api", "../OpenERP.Api/OpenERP.Api.csproj");
        var apiImage = $"api:{runId}";
        var web = builder.AddViteApp("web", "../OpenERP.Web")
            .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"]);
        api.AddEFMigrations("migrations")
            .WithMigrationsProject("../OpenERP.Migrations.PostgreSQL/OpenERP.Migrations.PostgreSQL.csproj")
            .PublishAsMigrationBundle(targetRuntime: "linux-x64", publishContainer: false);

        var openerp = builder.AddDockerfile(Image, staging)
            .WithImageTag(LocalTag)
            .WithBuildArg(BaseImageArgument, baseImage)
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Ports.Add("127.0.0.1:${WEB_PORT:-8080}:8000");
                service.Init = true;
                service.Restart = "unless-stopped";
                service.StopGracePeriod = "120s";
                service.StopSignal = "SIGTERM";
                service.AddVolume(BindMount("./data/postgres", "/var/lib/postgresql"));
                service.AddVolume(BindMount("./data", "/var/lib/openerp"));
            });

#pragma warning disable ASPIREPIPELINES003 // 固定发布镜像架构，并为本次 API 构建分配独立本地标签。
        api.WithContainerBuildOptions(options =>
        {
            options.LocalImageTag = runId;
            options.TargetPlatform = ContainerTargetPlatform.LinuxAmd64;
        });
        web.WithContainerBuildOptions(options => options.TargetPlatform = ContainerTargetPlatform.LinuxAmd64);
        openerp.WithContainerBuildOptions(options => options.TargetPlatform = ContainerTargetPlatform.LinuxAmd64);
#pragma warning restore ASPIREPIPELINES003

        var composeImage = $"{Image}:{LocalTag}";
        if (builder.Configuration["ReleaseTag"] is { Length: > 0 } releaseTag)
        {
            var endpoint = builder.AddParameter("registry-endpoint");
            var repository = builder.AddParameter("registry-repository");
#pragma warning disable ASPIRECOMPUTE003 // 用发布参数配置最终镜像的容器仓库。
            builder.AddContainerRegistry("registry", endpoint, repository);
#pragma warning restore ASPIRECOMPUTE003

#pragma warning disable ASPIREPIPELINES003 // 将最终镜像与 Release 标签绑定。
            openerp.WithRemoteImageTag(releaseTag);
#pragma warning restore ASPIREPIPELINES003
            builder.Eventing.Subscribe<BeforeStartEvent>(async (_, cancellationToken) =>
            {
                composeImage = await ((IValueProvider)new ContainerImageReference(openerp.Resource))
                    .GetValueAsync(cancellationToken) ?? throw new InvalidOperationException("缺少 OpenERP 发布镜像名。");
            });
        }

        // API 和 Web 保留各自的 Aspire 构建步骤。Compose 只部署组装后的镜像。
        compose.ConfigureComposeFile(file =>
        {
            file.Services.Remove("api");
            file.Services.Remove("web");
            file.Services[Image].Image = composeImage;
        }).ConfigureEnvFile(env =>
        {
            env.Remove("API_IMAGE");
            env.Remove("WEB_IMAGE");
            env.Remove("OPENERP_IMAGE");
        });

        ILogger? baseBuildLogger = null;
        if (buildBaseLocally)
        {
            openerp.WithPipelineStepFactory("build-openerp-base", async context =>
            {
                await SingleContainerArtifacts.BuildBaseImageAsync(baseImage, staging, deploy, context.CancellationToken);
                baseBuildLogger = context.Logger;
            }, dependsOn: [WellKnownPipelineSteps.CheckContainerRuntime], requiredBy: ["build-openerp"]);
        }

        openerp.WithPipelineStepFactory(
            "stage-openerp",
            context => SingleContainerArtifacts.StageAsync(context, staging, deploy, apiImage),
            dependsOn: ["build-api", "build-web", BundleStep],
            requiredBy: ["build-openerp"]);
        openerp.WithPipelineConfiguration(context =>
        {
            const string publish = WellKnownPipelineSteps.Publish;
            const string push = WellKnownPipelineSteps.Push;
            const string pushImage = WellKnownPipelineTags.PushContainerImage;
            context.Steps.Single(step => step.Name == BundleStep).RequiredBySteps.Remove(publish);
            foreach (var step in context.GetSteps(pushImage)
                         .Where(step => step.Resource?.Name is "api" or "web"))
            {
                step.RequiredBySteps.Remove(push);
            }
        });

        try
        {
            await builder.Build().RunAsync();
        }
        finally
        {
            if (baseBuildLogger is not null)
                await SingleContainerArtifacts.RemoveBaseImageAsync(baseBuildLogger, baseImage);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static Volume BindMount(string source, string target) => new()
    {
        Name = source,
        Type = "bind",
        Source = source,
        Target = target
    };
}
