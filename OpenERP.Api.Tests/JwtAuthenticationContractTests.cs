using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenERP.Api.Authentication;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class JwtAuthenticationContractTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await fixture.EnsureApiStartedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Login_issues_a_bearer_token_and_refresh_cookie_cannot_access_business()
    {
        // Given 已初始化的管理员与独立浏览器。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.RefreshCsrfAsync(client, ct);

        // When 使用管理员密码登录。
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = fixture.AdminPassword }, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Then JWT 可以访问业务，持久刷新 Cookie 单独访问业务时被拒绝。
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(tokens.TryGetProperty("accessToken", out var accessToken), "登录应签发 access JWT。");
        var rawToken = Assert.IsType<string>(accessToken.GetString());
        var segments = rawToken.Split('.');
        Assert.Equal(3, segments.Length);
        var jwt = new JsonWebToken(rawToken);
        Assert.Equal(TimeSpan.FromMinutes(20), jwt.ValidTo - jwt.IssuedAt);
        Assert.Equal(jwt.ValidTo, tokens.GetProperty("accessTokenExpiresAt").GetDateTimeOffset().UtcDateTime);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        using var bearer = await client.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.OK, bearer.StatusCode);

        segments[2] = (segments[2][0] == 'A' ? "B" : "A") + segments[2][1..];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', segments));
        using var tampered = await client.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("Cookie");
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        using var cookieOnly = await client.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, cookieOnly.StatusCode);
        Assert.Equal("Bearer", Assert.Single(cookieOnly.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task Persisted_refresh_cookie_restores_the_same_login_after_an_api_restart()
    {
        // Given 已登录的浏览器持有 refresh Cookie 和匿名防伪令牌。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        var account = await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);

        // When 重启 API 后使用原 Cookie 刷新。
        await fixture.RestartApiAsync(ct);
        client.DefaultRequestHeaders.Authorization = null;
        using var refresh = await client.PostAsync("/api/auth/refresh", null, ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Then 仍是原登录和固定到期时间，新 JWT 可访问业务。
        Assert.False(refresh.Headers.Contains("Set-Cookie"));
        var tokens = await refresh.Content.ReadFromJsonAsync<JsonElement>(ct);
        var refreshedAccount = tokens.GetProperty("account");
        Assert.Equal(account.GetProperty("lastLoginAt").GetDateTimeOffset(), refreshedAccount.GetProperty("lastLoginAt").GetDateTimeOffset());
        Assert.Equal(account.GetProperty("expiresAt").GetDateTimeOffset(), refreshedAccount.GetProperty("expiresAt").GetDateTimeOffset());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        using var business = await client.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.OK, business.StatusCode);
    }

    [Fact]
    public async Task Cli_reset_rejects_both_devices_previously_issued_access_and_refresh_tokens()
    {
        // Given 两台设备各持有一组有效的 access JWT 和 refresh Cookie。
        var ct = TestContext.Current.CancellationToken;
        using var first = fixture.CreateApiClient();
        using var second = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(first, ct, fixture.AdminPassword);
        await AuthenticationIntegrationTests.LoginAsync(second, ct, fixture.AdminPassword);
        foreach (var device in new[] { first, second })
        {
            using var working = await device.GetAsync("/api/ping", ct);
            Assert.Equal(HttpStatusCode.OK, working.StatusCode);
        }

        var connection = await fixture.GetPostgresConnectionStringAsync(ct);
        try
        {
            // When 独立 CLI 进程重置管理员密码。
            var reset = await AuthenticationStartupTests.RunApiOnDatabaseAsync(
                connection, " reset-for-jwt ", ["reset-admin-password", "--stdin"], ct);
            Assert.Equal(0, reset.ExitCode);

            // Then 两台设备的旧 JWT 均不能访问业务，也不能通过旧 refresh 恢复。
            foreach (var device in new[] { first, second })
            {
                using var rejected = await device.GetAsync("/api/ping", ct);
                Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
                device.DefaultRequestHeaders.Authorization = null;
                using var refresh = await device.PostAsync("/api/auth/refresh", null, ct);
                Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            }
        }
        finally
        {
            var restored = await AuthenticationStartupTests.RunApiOnDatabaseAsync(
                connection, fixture.AdminPassword, ["reset-admin-password", "--stdin"], CancellationToken.None);
            Assert.Equal(0, restored.ExitCode);
        }
    }
}

public sealed class AccessTokenLifetimeTests
{
    [Fact]
    public async Task Expired_access_cannot_pass_validation_during_the_default_clock_skew_window()
    {
        // Given 刷新登录仍有效，先签发一个已经过期一分钟的 JWT。
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        var tokens = new AccessTokens(builder.Configuration, builder.Environment);
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-21);
        var login = new AdminRefreshToken { Id = Guid.NewGuid(), ExpiresAt = issuedAt.AddHours(24) };
        var expired = tokens.Issue(login, issuedAt);
        Assert.NotNull(expired);

        // When 使用生产注册所用的验证参数验证；Then 框架报告令牌过期。
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(expired.Value.Value, tokens.ValidationParameters());
        Assert.False(validation.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(validation.Exception);
    }

    [Fact]
    public void Access_issued_near_the_refresh_deadline_does_not_outlive_the_login()
    {
        // Given 刷新期限不足 20 分钟。
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        var tokens = new AccessTokens(builder.Configuration, builder.Environment);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var deadline = now.AddMinutes(3);
        var login = new AdminRefreshToken { Id = Guid.NewGuid(), ExpiresAt = deadline };

        // When 签发新的 access JWT；Then 它与 refresh 在同一时刻到期。
        var token = tokens.Issue(login, now);
        Assert.NotNull(token);
        Assert.Equal(deadline.UtcDateTime, new JsonWebToken(token.Value.Value).ValidTo);
    }
}
