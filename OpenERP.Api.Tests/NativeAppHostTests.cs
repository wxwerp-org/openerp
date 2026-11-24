using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class NativeAppHostTests
{
    [Fact]
    public async Task Native_mode_runs_api_and_web_without_container_resources()
    {
        // Given 用户选择在这台电脑上原生运行，When 读取 AppHost 资源图。
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1", "OpenERP:Mode=Native"], TestContext.Current.CancellationToken);

        // Then 网页和后端均存在，启动过程中无需拉取任何容器。
        Assert.Contains(builder.Resources, resource => resource.Name == "api");
        Assert.Contains(builder.Resources, resource => resource.Name == "web");
        Assert.Empty(builder.Resources.OfType<ContainerResource>());
    }
}
