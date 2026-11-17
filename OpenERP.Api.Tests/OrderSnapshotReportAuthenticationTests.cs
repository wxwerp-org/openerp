using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSnapshotReportAuthenticationTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await fixture.EnsureApiStartedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Report_requires_login_and_is_registered_in_the_real_application()
    {
        // Given 未登录的客户端；When 读取报表；Then 与其它业务接口一样拒绝匿名访问。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        using var anonymous = await client.GetAsync("/api/reports/order-snapshot", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // When 管理员登录后读取；Then 实际组合根提供报表实现并返回UTC查询时间。
        await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);
        using var authenticated = await client.GetAsync("/api/reports/order-snapshot", ct);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var body = await authenticated.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.EndsWith("Z", body.GetProperty("generatedAtUtc").GetString());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.True(authenticated.Headers.CacheControl?.NoStore);
    }
}
