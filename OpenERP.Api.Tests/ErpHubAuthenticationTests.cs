using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ErpHubAuthenticationTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await fixture.EnsureApiStartedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Start_shop_sync_requires_an_active_login_session()
    {
        // Given 已登录管理员的 Hub 连接建立，随后该登录会话被撤销。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);
        var token = client.DefaultRequestHeaders.Authorization?.Parameter;
        Assert.False(string.IsNullOrWhiteSpace(token));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.ApiEndpoint, "/api/hubs/erp"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            })
            .Build();
        await connection.StartAsync(ct);

        await AuthenticationIntegrationTests.RefreshCsrfAsync(client, ct);
        using var logout = await client.PostAsync("/api/auth/logout", null, ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // When 已撤销会话的旧连接调用同步命令。
        var exception = await Record.ExceptionAsync(
            () => connection.InvokeAsync<ShopSyncStartResult>("StartShopSync", ct));

        // Then 命令被拒绝，连接被断开，不能绕过 HTTP 会话撤销。
        Assert.NotNull(exception);
        await TestWait.UntilAsync(
            () => connection.State == HubConnectionState.Disconnected,
            "撤销会话后 Hub 连接未断开",
            ct);
    }

    [Fact]
    public async Task Hub_negotiate_accepts_query_token_only_on_hub_paths()
    {
        // Given 已登录管理员取得 access JWT。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);
        var token = client.DefaultRequestHeaders.Authorization?.Parameter;
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = null;
        var negotiate = $"/api/hubs/erp/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token!)}";

        // When 仅用查询参数完成 Hub negotiate。
        using var accepted = await client.PostAsync(negotiate, null, ct);

        // Then 认证成功并返回连接令牌。
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(
            body.GetProperty("connectionToken").GetString())));

        // When 不带 token、或把 query token 放到普通接口。
        using var anonymous = await fixture.CreateApiClient()
            .PostAsync("/api/hubs/erp/negotiate?negotiateVersion=1", null, ct);
        using var wrongPath = await client.GetAsync($"/api/ping?access_token={Uri.EscapeDataString(token!)}", ct);

        // Then 两者都被拒绝，证明 query token 只对 Hub 路径生效。
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPath.StatusCode);

        // When 使用 Authorization 头连接 Hub。
        using var bearerClient = fixture.CreateApiClient();
        bearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var bearer = await bearerClient.PostAsync(
            "/api/hubs/erp/negotiate?negotiateVersion=1", null, ct);

        // Then 原有 Bearer 认证仍然有效。
        Assert.Equal(HttpStatusCode.OK, bearer.StatusCode);
    }
}
