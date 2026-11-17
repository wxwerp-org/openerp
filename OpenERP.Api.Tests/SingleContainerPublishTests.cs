using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class SingleContainerPublishTests
{
    [Fact]
    public async Task Publishing_contains_one_workload_with_three_persistent_volumes()
    {
        // Given 使用实际 AppHost 的发布模式，不启动开发资源。
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["--operation=publish", "NO_PROXY=localhost,127.0.0.1,::1"], TestContext.Current.CancellationToken);
        Assert.True(builder.ExecutionContext.IsPublishMode);

        // When 查看将交付的资源，Then 前后端与基础服务只产生一个容器工作负载。
        var container = Assert.Single(builder.Resources.OfType<ContainerResource>());
        Assert.Equal("openerp", container.Name);
        Assert.DoesNotContain(builder.Resources, resource => resource is ProjectResource);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name is "web" or "postgres" or "redis" or "seaweedfs");

        // Then 三种持久状态分别挂载，密钥目录只读，只有应用 HTTP 入口。
        var mounts = container.Annotations.OfType<ContainerMountAnnotation>().ToArray();
        Assert.Equal(new[] { "/data", "/var/lib/postgresql", "/var/lib/redis" },
            mounts.Where(mount => mount.Type == ContainerMountType.Volume).Select(mount => mount.Target).Order().ToArray());
        Assert.Contains(mounts, mount => mount.Target == "/run/secrets" && mount.IsReadOnly);
        var endpoint = Assert.Single(container.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(8081, endpoint.TargetPort);
    }
}
