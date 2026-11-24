using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AppHostStorageSelectionTests
{
    [Theory]
    [InlineData("Database:Provider", "Sqlite", "postgres", "redis", "seaweedfs")]
    [InlineData("Cache:Provider", "Memory", "redis", "postgres", "seaweedfs")]
    [InlineData("ObjectStorage:Provider", "Local", "seaweedfs", "postgres", "redis")]
    public async Task Selects_each_local_provider_independently(
        string key, string provider, string omittedResource, string firstRetained, string secondRetained)
    {
        // Given 开发宿主配置中只切换一个 provider；When 构建实际 AppHost 资源图。
        var ct = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1", $"--{key}={provider}",
                "--ConnectionStrings:openerp=Data Source=openerp-test.db", "--ObjectStorage:RootPath=openerp-test-objects"], ct);

        // Then 对应的容器资源消失，其余依赖及应用资源保持可用。
        var names = builder.Resources.Select(resource => resource.Name).ToArray();
        Assert.DoesNotContain(omittedResource, names);
        Assert.Contains(firstRetained, names);
        Assert.Contains(secondRetained, names);
        Assert.Contains("api", names);
        Assert.Contains("web", names);
    }
}
