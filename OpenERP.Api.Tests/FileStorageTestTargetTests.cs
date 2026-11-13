using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FileStorageTestTargetTests
{
    private const string ApiDescription = """
        {
          "resources": [{ "name": "api", "urls": [
            { "name": "https", "url": "https://localhost:17443" },
            { "name": "http", "url": "http://localhost:17080" },
            { "name": "http", "url": "http://LOCALHOST:17080/" }
          ] }]
        }
        """;

    [Theory]
    [InlineData("https://localhost:17444/")]
    [InlineData("https://localhost:17443/other/")]
    public async Task Rejects_an_API_URL_that_does_not_match_the_selected_AppHost(string configuredUrl)
    {
        // Given 选中的 AppHost 为 api 分配了 https://localhost:17443/。
        // When 测试 URL 指向另一个端口或非根路径。
        // Then 在获得可用于上传、删除或重启前置访问的客户端之前拒绝此配置。
        var commands = new List<string>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = await FileStorageIntegrationTests.CreateApiClientAsync(configuredUrl, arguments =>
            {
                commands.Add(string.Join(' ', arguments));
                return Task.FromResult(ApiDescription);
            });
        });

        Assert.Contains("OPENERP_TEST_API_URL", error.Message);
        Assert.Contains("https://localhost:17443", error.Message);
        Assert.Equal(["describe api --format Json"], commands);
    }

    [Theory]
    [InlineData("https://localhost:17443", "https://localhost:17443/")]
    [InlineData("https://LOCALHOST:17443/", "https://localhost:17443/")]
    [InlineData("http://localhost:17080", "http://localhost:17080/")]
    public async Task Accepts_an_equivalent_API_root_URL(string configuredUrl, string expectedUrl)
    {
        // Given 选中的 AppHost 为 api 同时分配了 HTTP 和 HTTPS 地址，且 HTTP 地址重复列出。
        // When 测试 URL 与其中一个根地址等价，允许末尾斜杠和主机大小写差异。
        // Then 等待该 AppHost 的 api 健康后才返回指向实际根地址的客户端。
        var commands = new List<string>();
        using var client = await FileStorageIntegrationTests.CreateApiClientAsync(configuredUrl,
            arguments =>
            {
                commands.Add(string.Join(' ', arguments));
                return Task.FromResult(ApiDescription);
            });

        Assert.Equal(new Uri(expectedUrl), client.BaseAddress);
        Assert.Equal(["describe api --format Json", "wait api --timeout 120"], commands);
    }

    [Fact]
    public async Task Matches_the_API_display_name_when_Aspire_adds_an_instance_suffix()
    {
        // Given Aspire 为 api 实例分配了带后缀的名称，资源显示名仍是 api。
        const string description = """
            {
              "resources": [{
                "name": "api-fwbsryss",
                "displayName": "api",
                "resourceType": "Project",
                "urls": [{ "name": "https", "url": "https://localhost:50110" }]
              }]
            }
            """;

        // When 测试 URL 指向该实例的实际 HTTPS 根地址。
        using var client = await FileStorageIntegrationTests.CreateApiClientAsync("https://localhost:50110",
            _ => Task.FromResult(description));

        // Then 测试可以使用该实例的实际地址访问 api。
        Assert.Equal(new Uri("https://localhost:50110/"), client.BaseAddress);
    }

    [Theory]
    [InlineData("""{ "resources": [{ "name": "web", "urls": [{ "url": "https://localhost:17443" }] }] }""")]
    [InlineData("""{ "resources": [{ "name": "api", "urls": [{ "url": "https://localhost:17443", "isInternal": true }] }] }""")]
    public async Task Rejects_a_URL_when_the_AppHost_has_no_accessible_API_endpoint(string description)
    {
        // Given 选中的 AppHost 没有 api，或仅报告了内部端点。
        // When 测试 URL 与另一个资源或内部端点的地址相同。
        // Then 在等待或访问该地址之前失败。
        var commands = new List<string>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = await FileStorageIntegrationTests.CreateApiClientAsync("https://localhost:17443", arguments =>
            {
                commands.Add(string.Join(' ', arguments));
                return Task.FromResult(description);
            });
        });

        Assert.Contains("Aspire describe", error.Message);
        Assert.Equal(["describe api --format Json"], commands);
    }

    [Fact]
    public async Task Waits_for_storage_and_then_the_API_after_restarting_SeaweedFS()
    {
        // Given 文件已上传到选中 AppHost 的 api。
        // When 测试重启该 AppHost 的 SeaweedFS。
        // Then 在恢复文件操作前依次等待 SeaweedFS 和 api 健康。
        var commands = new List<string>();
        await FileStorageIntegrationTests.RestartStorageAsync(arguments =>
        {
            commands.Add(string.Join(' ', arguments));
            return Task.FromResult(string.Empty);
        });

        Assert.Equal(["resource seaweedfs restart", "wait seaweedfs --timeout 120", "wait api --timeout 120"], commands);
    }
}
