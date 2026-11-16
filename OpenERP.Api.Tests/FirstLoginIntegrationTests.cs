using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FirstLoginIntegrationTests
{
    [Fact]
    public async Task First_login_requires_password_change_before_business_and_profile_access()
    {
        // Given 新启动的隔离数据库通过初始密码创建了管理员，三个设备还未登录。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        using var first = fixture.CreateApiClient();
        using var second = fixture.CreateApiClient();
        using var third = fixture.CreateApiClient();
        var password = fixture.AdminPassword;

        // When 使用初始密码登录并尝试访问业务、修改资料和退出。
        var initial = await AuthenticationIntegrationTests.LoginAsync(first, ct, password);
        await AuthenticationIntegrationTests.LoginAsync(second, ct, password);
        await AuthenticationIntegrationTests.LoginAsync(third, ct, password);
        using var ping = await first.GetAsync("/api/ping", ct);
        using var profile = await first.PutAsJsonAsync("/api/account/profile", new { name = "更改姓名" }, ct);
        using var logout = await third.PostAsync("/api/auth/logout", null, ct);

        // Then 登录状态、退出和 CSRF 可用，业务和资料接口报告必须改密。
        foreach (var blocked in new[] { ping, profile })
        {
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("password_change_required", problem.GetProperty("code").GetString());
        }

        Assert.True(initial.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var me = await first.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(initial.GetProperty("name").GetString(), me.GetProperty("name").GetString());

        using var unchanged = await first.PostAsJsonAsync("/api/auth/change-password",
            new { newPassword = password }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, unchanged.StatusCode);
        var unchangedProblem = await unchanged.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("首次登录的新密码须与初始密码不同。", unchangedProblem.GetProperty("detail").GetString());

        // When 首次登录的管理员只提交新密码，凭已验证的受限会话完成设置。
        const string newPassword = " new-password ";
        using var changed = await first.PostAsJsonAsync("/api/auth/change-password",
            new { newPassword }, ct);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        foreach (var client in new[] { first, second })
        {
            using var stale = await client.GetAsync("/api/auth/me", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        }

        using var newDevice = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.RefreshCsrfAsync(newDevice, ct);
        using var rejected = await newDevice.PostAsJsonAsync("/api/auth/login", new { username = "admin", password }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var updated = await AuthenticationIntegrationTests.LoginAsync(newDevice, ct, newPassword);
        // Then 新会话可以访问完整功能，数据库清除首次改密状态。
        Assert.False(updated.GetProperty("mustChangePassword").GetBoolean());
        using var ready = await newDevice.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        // Given 已完成首次设置；When 普通改密省略原密码或伪造首次设置标记；Then 拒绝且密码不变。
        using var missingOld = await newDevice.PostAsJsonAsync("/api/auth/change-password",
            new { newPassword = "another-password", mustChangePassword = true }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, missingOld.StatusCode);
        using var verified = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(verified, ct, newPassword);

        // When 已撤销的首次会话再次尝试无原密码改密；Then 不能重复初始化。
        using var staleChange = await second.PostAsJsonAsync("/api/auth/change-password",
            new { newPassword = "another-password" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, staleChange.StatusCode);
    }
}
