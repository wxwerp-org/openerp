using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.EntityFrameworkCore;
using Aspire.Hosting.Pipelines;

namespace OpenERP.AppHost;

internal static class ComposeDeployment
{
    private const string Image = "openerp";
    private const string LocalTag = "local";
    private const string BundleStep = "migrations-generate-migration-bundle";

    internal static void Run(IDistributedApplicationBuilder builder)
    {
        var runId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(builder.AppHostDirectory, "obj", "single-container-build", runId);
        var deploy = Path.GetFullPath("../deploy", builder.AppHostDirectory);
        var compose = builder.AddDockerComposeEnvironment("compose").WithDashboard(false);

        var api = builder.AddProject<Projects.OpenERP_Api>("api");
        var apiImage = $"api:{runId}";
#pragma warning disable ASPIREPIPELINES003 // 为 SDK 的本地构建产物分配本次流水线专用标签。
        api.WithContainerBuildOptions(options => options.LocalImageTag = runId);
#pragma warning restore ASPIREPIPELINES003
        builder.AddViteApp("web", "../OpenERP.Web")
            .WithPnpm(installArgs: ["--frozen-lockfile", "--config.confirmModulesPurge=false"]);
        api.AddEFMigrations("migrations")
            .PublishAsMigrationBundle(targetRuntime: "linux-x64", publishContainer: false);

        var openerp = builder.AddDockerfile(Image, staging)
            .WithImageTag(LocalTag)
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

        var composeImage = $"{Image}:{LocalTag}";
        if (builder.Configuration["ReleaseTag"] is { Length: > 0 } releaseTag)
        {
#pragma warning disable ASPIRECOMPUTE003 // 使用 Aspire 的发布仓库参数解析最终镜像名。
            var endpoint = builder.AddParameter("registry-endpoint");
            var repository = builder.AddParameter("registry-repository");
            builder.AddContainerRegistry("registry", endpoint, repository);
#pragma warning restore ASPIRECOMPUTE003

#pragma warning disable ASPIREPIPELINES003 // 让 Compose 与 Aspire 使用相同的发布标签。
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

#pragma warning disable ASPIREPIPELINES001 // 声明组装依赖，发布 Compose 时无需先构建运行制品。
        openerp.WithPipelineStepFactory(
            "stage-openerp",
            context => SingleContainerArtifacts.StageAsync(context, staging, deploy, apiImage),
            dependsOn: ["build-api", "build-web", BundleStep],
            requiredBy: ["build-openerp"]);
        openerp.WithPipelineConfiguration(context =>
        {
            context.Steps.Single(step => step.Name == BundleStep).RequiredBySteps.Remove(WellKnownPipelineSteps.Publish);
            foreach (var step in context.GetSteps(WellKnownPipelineTags.PushContainerImage)
                         .Where(step => step.Resource?.Name is "api" or "web"))
            {
                step.RequiredBySteps.Remove(WellKnownPipelineSteps.Push);
            }
        });
#pragma warning restore ASPIREPIPELINES001

        try
        {
            builder.Build().Run();
        }
        finally
        {
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
