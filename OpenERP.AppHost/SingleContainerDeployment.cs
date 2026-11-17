using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace OpenERP.AppHost;

internal static class SingleContainerDeployment
{
    internal static void AddSingleContainerDeployment(this IDistributedApplicationBuilder builder)
    {
        builder.AddDockerComposeEnvironment("compose")
            .WithProperties(environment => environment.DashboardEnabled = false);

        var allowedHosts = builder.AddParameter("allowed-hosts");
        var proxy = builder.AddParameter("trusted-proxy");
        var secretsDirectory = builder.Configuration["Deployment:SecretsDirectory"] ?? "/etc/openerp/secrets";

#pragma warning disable ASPIREPIPELINES003 // 发布镜像已明确只支持 linux/amd64，需要固定构建平台。
        builder.AddDockerfile("openerp", "..", "deploy/container/Dockerfile")
            .WithVolume("openerp-postgres", "/var/lib/postgresql")
            .WithVolume("openerp-redis", "/var/lib/redis")
            .WithVolume("openerp-seaweedfs", "/data")
            .WithBindMount(secretsDirectory, "/run/secrets", isReadOnly: true)
            .WithHttpEndpoint(targetPort: 8081, name: "http")
            .WithEnvironment("AllowedHosts", allowedHosts)
            .WithEnvironment("Deployment__KnownProxies__0", proxy)
            .WithEnvironment("OPENERP_APPLY_MIGRATIONS", "0")
            .WithContainerBuildOptions(options => options.TargetPlatform = ContainerTargetPlatform.LinuxAmd64)
            .PublishAsDockerComposeService((_, service) =>
            {
                service.Ports = ["${OPENERP_BIND_ADDRESS:-127.0.0.1}:${OPENERP_HTTP_PORT:-8081}:8081"];
                service.StopGracePeriod = "180s";
                service.Restart = "on-failure:3";
                // s6-overlay 会从 /run/s6 执行运行期入口；Docker tmpfs 默认 noexec。
                service.Tmpfs = ["/run:rw,exec,nosuid,nodev,size=64m"];
                service.Environment["OPENERP_APPLY_MIGRATIONS"] = "${OPENERP_APPLY_MIGRATIONS:-0}";
            });
#pragma warning restore ASPIREPIPELINES003
    }
}
