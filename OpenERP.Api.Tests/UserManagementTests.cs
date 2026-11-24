using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenERP.Api.Authentication;
using OpResult;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class UserManagementTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private const string Password = "new-user-123";

    public async ValueTask InitializeAsync() =>
        await fixture.EnsureApiStartedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Only_administrator_manages_users_and_employee_can_update_own_profile_and_password()
    {
        // Given 管理员已登录，员工账号尚未创建。
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateApiClient();
        using var user = fixture.CreateApiClient();
        var adminAccount = await AuthenticationIntegrationTests.LoginAsync(admin, ct, fixture.AdminPassword);

        // When 管理员创建员工，额外提交管理员角色字段。
        var username = $"u{Guid.NewGuid():N}"[..16];
        var created = await CreateAsync(admin, username, ct);
        var id = created.GetProperty("id").GetGuid();
        var url = $"/api/users/{id:D}";

        // Then 响应只包含员工资料，员工可登录，但无法访问用户管理接口。
        Assert.Equal(7, id.Version);
        Assert.NotEqual(default, created.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("User", created.GetProperty("role").GetString());
        Assert.False(created.TryGetProperty("passwordHash", out _));
        Assert.False(created.TryGetProperty("password", out _));
        var tokenResponse = await AuthenticationIntegrationTests.LoginAsync(user, ct, Password, username);
        Assert.Equal(id, tokenResponse.GetProperty("id").GetGuid());
        var jwt = new JsonWebToken(user.DefaultRequestHeaders.Authorization!.Parameter!);
        Assert.Equal(id.ToString("D"), jwt.Subject);
        Assert.Equal("User", jwt.GetClaim("role").Value);
        Assert.Equal("Administrator", adminAccount.GetProperty("role").GetString());
        var adminUrl = $"/api/users/{adminAccount.GetProperty("id").GetGuid():D}";
        using var protectedAdmin = await admin.PutAsJsonAsync(adminUrl, new { username = "renamed", name = "管理员", role = "User" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, protectedAdmin.StatusCode);
        using var protectedDelete = await admin.DeleteAsync(adminUrl, ct);
        Assert.Equal(HttpStatusCode.Conflict, protectedDelete.StatusCode);

        using var list = await admin.GetAsync("/api/users", ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains((await list.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray(),
            entry => entry.GetProperty("id").GetGuid() == id);
        using var detail = await admin.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var forbiddenList = await user.GetAsync("/api/users", ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);
        using var forbiddenRead = await user.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        using var forbiddenCreate = await user.PostAsJsonAsync("/api/users", new { username }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
        using var forbiddenEdit = await user.PutAsJsonAsync(url, new { username }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEdit.StatusCode);
        using var forbiddenDelete = await user.DeleteAsync(url, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        using var profile = await user.PutAsJsonAsync("/api/account/profile",
            new { name = "员工姓名", id = adminAccount.GetProperty("id").GetGuid(), role = "Administrator" }, ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var ownProfile = await profile.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(id, ownProfile.GetProperty("id").GetGuid());
        Assert.Equal("员工姓名", ownProfile.GetProperty("name").GetString());
        Assert.Equal("User", ownProfile.GetProperty("role").GetString());
        using var adminProfile = await admin.GetAsync("/api/auth/me", ct);
        var persistedAdmin = await adminProfile.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(adminAccount.GetProperty("name").GetString(), persistedAdmin.GetProperty("name").GetString());

        const string newPassword = "updated-password";
        using var passwordChange = await user.PostAsJsonAsync("/api/auth/change-password",
            new { oldPassword = Password, newPassword }, ct);
        Assert.Equal(HttpStatusCode.NoContent, passwordChange.StatusCode);
        await AuthenticationIntegrationTests.LoginAsync(user, ct, newPassword, username);
        using var adminStillActive = await admin.GetAsync("/api/orders", ct);
        Assert.Equal(HttpStatusCode.OK, adminStillActive.StatusCode);
    }

    [Fact]
    public async Task Rename_preserves_identity_and_deletion_revokes_sessions_but_reserves_username_for_seven_days()
    {
        // Given 管理员与一个拥有有效 JWT 和刷新凭证的员工。
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateApiClient();
        using var user = fixture.CreateApiClient();
        using var otherUser = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(admin, ct, fixture.AdminPassword);
        var prefix = $"u{Guid.NewGuid():N}"[..16];
        var before = prefix + "a";
        var after = prefix + "b";
        var id = (await CreateAsync(admin, before, ct)).GetProperty("id").GetGuid();
        var url = $"/api/users/{id:D}";
        await AuthenticationIntegrationTests.LoginAsync(user, ct, Password, before);

        // When 将员工改名；Then 旧名字立即可用，原会话指向同一 UUID。
        using var rename = await admin.PutAsJsonAsync(url, new { username = after, name = "员工" }, ct);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(id, (await rename.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
        var otherId = (await CreateAsync(admin, before, ct)).GetProperty("id").GetGuid();
        await AuthenticationIntegrationTests.LoginAsync(otherUser, ct, Password, before);
        using var originalProfile = await user.GetAsync("/api/auth/me", ct);
        var originalAccount = await originalProfile.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(id, originalAccount.GetProperty("id").GetGuid());
        Assert.Equal(after, originalAccount.GetProperty("username").GetString());
        using var refreshed = await AuthenticationIntegrationTests.PostAnonymouslyAsync(user, "/api/auth/refresh", null, ct);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshedBody = await refreshed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(id.ToString("D"), new JsonWebToken(refreshedBody.GetProperty("accessToken").GetString()).Subject);

        // When 删除员工；Then 原行保留，原登录与会话不可用，其他账号可继续操作。
        using var deletion = await admin.DeleteAsync(url, ct);
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        using var detail = await admin.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var listing = await admin.GetFromJsonAsync<JsonDocument>("/api/users", ct);
        Assert.DoesNotContain(listing!.RootElement.EnumerateArray(), entry => entry.GetProperty("id").GetGuid() == id);
        using var unauthorized = await user.GetAsync("/api/orders", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var failedRefresh = await AuthenticationIntegrationTests.PostAnonymouslyAsync(user, "/api/auth/refresh", null, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, failedRefresh.StatusCode);
        using var continuing = await otherUser.GetAsync("/api/orders", ct);
        Assert.Equal(HttpStatusCode.OK, continuing.StatusCode);
        using var failedLogin = await AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(user, "/api/auth/login",
            new { username = after, password = Password }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);

        using var reserved = await admin.PostAsJsonAsync("/api/users", new { username = after, name = "员工", password = Password }, ct);
        Assert.Equal(HttpStatusCode.Conflict, reserved.StatusCode);
        using var reservedRename = await admin.PutAsJsonAsync($"/api/users/{otherId:D}", new { username = after, name = "员工" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, reservedRename.StatusCode);

        // Given PostgreSQL 中保留了原用户及指向原用户的已撤销会话。
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseOpenErpPostgres(await fixture.GetPostgresConnectionStringAsync(ct)).Options;
        await using var db = new AppDbContext(options);
        var original = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(account => account.Id == id, ct);
        Assert.Equal(after, original.Username);
        Assert.True(await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(
            token => token.UserId == id && token.DeletedAt != null, ct));
        var availableAt = Assert.IsType<DateTimeOffset>(original.DeletedAt).AddDays(7);
        var clock = new TestClock(availableAt.AddTicks(-10));
        var store = new AccountStore(db, options, new PasswordHasher<UserAccount>(), clock);
        var changes = new ProfileChanges("员工", null, null);

        // When 距离七天还差 PostgreSQL 可表达的最小单位；Then 仍禁止新建和改名。
        Assert.True((await store.CreateUserAsync(after, Password, changes, ct)).IsErr);
        var beforeExpiry = await store.UpdateUserAsync(otherId, after, changes, ct);
        Assert.Equal(409, beforeExpiry.Status);

        // When 刚满七天；Then 可以改名，也可以新建。但新用户获得独立 UUID。
        clock.UtcNow = availableAt;
        var availableRename = await store.UpdateUserAsync(otherId, after, changes, ct);
        Assert.Equal(200, availableRename.Status);
        Assert.Equal(otherId, availableRename.Result.Match(account => account.Id, _ => Guid.Empty));
        var restoreName = await store.UpdateUserAsync(otherId, before, changes, ct);
        Assert.Equal(200, restoreName.Status);
        var replacement = await store.CreateUserAsync(after, Password, changes, ct);
        var replacementId = replacement.Match(account => account.Id, _ => Guid.Empty);
        Assert.Equal(7, replacementId.Version);
        Assert.NotEqual(id, replacementId);
        using var originalDetail = await admin.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.NotFound, originalDetail.StatusCode);
    }

    private static async Task<JsonElement> CreateAsync(HttpClient admin, string username, CancellationToken ct)
    {
        using var created = await admin.PostAsJsonAsync("/api/users",
            new { username, name = "员工", password = Password, role = "Administrator" }, ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private sealed class TestClock(DateTimeOffset utcNow) : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
