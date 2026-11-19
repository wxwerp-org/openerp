using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ErpSettingsIntegrationTests
{
    [Fact]
    public async Task AdministratorCanReplaceTheTokenAndTheSettingSurvivesAnApiRestart()
    {
        // Given 全新数据库已初始化管理员，没有配置 ERP Token。
        const string route = "/api/erp/config";
        const string initialToken = "first-test-erp-token";
        const string updatedToken = "updated-test-erp-token";
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await fixture.EnsureApiStartedAsync(ct);
        using var client = fixture.CreateApiClient();
        using var anonymous = await client.PutAsJsonAsync(route, new { accessToken = initialToken }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);
        client.DefaultRequestHeaders.Remove(AuthenticationIntegrationTests.CsrfHeader);
        Assert.False(await IsConfiguredAsync(client, ct));

        // When 管理员保存 Token，再提交空值和含换行的无效更新。
        using var created = await client.PutAsJsonAsync(route, new { accessToken = initialToken }, ct);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        Assert.Empty(await created.Content.ReadAsStringAsync(ct));
        Assert.True(await IsConfiguredAsync(client, ct));

        var connectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using var database = new NpgsqlConnection(connectionString);
        await database.OpenAsync(ct);
        await using var readToken = new NpgsqlCommand("SELECT \"ProtectedAccessToken\" FROM \"ErpSettings\"", database);
        var initialStored = Assert.IsType<string>(await readToken.ExecuteScalarAsync(ct));
        Assert.DoesNotContain(initialToken, initialStored);
        foreach (var invalidToken in new[] { " ", "line\r\nbreak" })
        {
            using var invalid = await client.PutAsJsonAsync(route, new { accessToken = invalidToken }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(initialStored, await readToken.ExecuteScalarAsync(ct));
        await AssertUpstreamTokenAsync(connectionString, initialToken, ct);

        // When 保存新值并重启 API；Then 配置仍存在，且响应和存储都不含原文。
        using var updated = await client.PutAsJsonAsync(route, new { accessToken = updatedToken }, ct);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var updatedStored = Assert.IsType<string>(await readToken.ExecuteScalarAsync(ct));
        Assert.NotEqual(initialStored, updatedStored);
        Assert.DoesNotContain(updatedToken, updatedStored);
        await fixture.RestartApiAsync(ct);
        using var restarted = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(restarted, ct, fixture.AdminPassword);
        Assert.True(await IsConfiguredAsync(restarted, ct));
        await AssertUpstreamTokenAsync(connectionString, updatedToken, ct);

        // Given 旧 Token 无法解密。When 查询配置。Then 仍显示已配置，且管理员可替换旧值。
        await using var damageToken = new NpgsqlCommand(
            """UPDATE "ErpSettings" SET "ProtectedAccessToken" = 'invalid-protected-token'""", database);
        await damageToken.ExecuteNonQueryAsync(ct);
        Assert.True(await IsConfiguredAsync(restarted, ct));
        // When 选店需要解密损坏的凭证。Then 返回可重试的依赖错误，管理员仍可更换凭证。
        using var selection = await restarted.GetAsync("/api/erp/order-sync-settings/shops", ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, selection.StatusCode);
        var problem = await selection.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("读取私有 ERP AccessToken 失败，请稍后重试。", problem.GetProperty("title").GetString());
        using var recovered = await restarted.PutAsJsonAsync(route, new { accessToken = updatedToken }, ct);
        Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
        await AssertUpstreamTokenAsync(connectionString, updatedToken, ct);
    }

    private static async Task<bool> IsConfiguredAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync("/api/erp/config", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(payload.TryGetProperty("accessToken", out _));
        return payload.GetProperty("configured").GetBoolean();
    }

    private static async Task AssertUpstreamTokenAsync(string connectionString, string expected, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDataProtection().SetApplicationName("OpenERP").PersistKeysToDbContext<AppDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        using var http = new HttpClient(new ErpResponseHandler(expected));
        var tokens = new ErpTokenStore(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            provider.GetRequiredService<IDataProtectionProvider>());
        var token = await tokens.GetAsync(ct);
        if (token.IsErr)
            throw new Xunit.Sdk.XunitException(token.Error.Message);
        var erp = new WxwErpClient(http);
        var shops = await erp.GetShopsAsync(1, 20, null, token.Value, ct);
        Assert.Equal(200, shops.Status);
        var orders = await erp.ReadOrdersAsync(30, null, token.Value, ct);
        if (orders.IsErr)
            Assert.Fail(orders.Error.Message);
        else
            Assert.Equal(0, orders.Value.Total);
    }

    private sealed class ErpResponseHandler(string expected) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(expected, Assert.Single(request.Headers.GetValues("AccessToken")));
            var payload = request.RequestUri!.AbsolutePath switch
            {
                "/open-api/shop/get_shops" =>
                    """{"success":true,"data":{"items":[{"sid":"30","platform":"TemuPop"}]},"data_count":1,"page_count":1}""",
                "/open-api/shop/get_status" => """{"success":true,"data":{"isTokenOk":true}}""",
                "/open-api/order/get_list_page_count" => """{"success":true,"data":{"data_count":0,"page_count":0}}""",
                _ => throw new InvalidOperationException($"出现意外的 ERP 请求 {request.RequestUri.AbsolutePath}。")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
