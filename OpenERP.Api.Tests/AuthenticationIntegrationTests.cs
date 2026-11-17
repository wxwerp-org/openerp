using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class AuthenticationIntegrationTests(AspireIntegrationFixture fixture)
{
    private string AdminPassword => fixture.AdminPassword;
    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Fact]
    public async Task Health_and_csrf_are_anonymous_but_business_and_mutations_are_protected()
    {
        // Given 未登录且没有 CSRF token 的独立浏览器。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();

        // When 查询健康状态和业务，并尝试登录。
        using var health = await client.GetAsync("/health", ct);
        using var alive = await client.GetAsync("/alive", ct);
        using var ping = await client.GetAsync("/api/ping", ct);
        using var me = await client.GetAsync("/api/auth/me", ct);
        using var missingToken = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = AdminPassword }, ct);

        // Then 健康和 CSRF 正常，业务返回 401，缺少 CSRF 的登录返回 400。
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        await AssertProblemAsync(ping, HttpStatusCode.Unauthorized, ct);
        await AssertProblemAsync(me, HttpStatusCode.Unauthorized, ct);
        await AssertProblemAsync(missingToken, HttpStatusCode.BadRequest, ct);

        await RefreshCsrfAsync(client, ct);
        using var badPassword = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = AdminPassword.Trim() }, ct);
        await AssertProblemAsync(badPassword, HttpStatusCode.Unauthorized, ct);

        await LoginAsync(client, ct, AdminPassword);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        using var file = new MultipartFormDataContent { { new StringContent("file"), "file", "file.png" } };
        using var upload = await client.PostAsync("/api/files/", file, ct);
        await AssertProblemAsync(upload, HttpStatusCode.BadRequest, ct);
    }

    [Fact]
    public async Task Order_sync_settings_require_a_ready_session_and_csrf_for_writes()
    {
        // Given 独立浏览器尚未登录，准备提交有效的关闭设置。
        const string route = "/api/erp/order-sync-settings";
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        var settings = new { enabled = false, shopSids = Array.Empty<int>() };

        // When 匿名读取设置、读取选店列表和保存设置。
        using var anonymousSettings = await client.GetAsync(route, ct);
        using var anonymousShops = await client.GetAsync($"{route}/shops", ct);
        using var anonymousSave = await client.PutAsJsonAsync(route, settings, ct);

        // Then 三个接口都要求登录。
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousSettings.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousShops.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousSave.StatusCode);

        // When 已登录的管理员移除 CSRF 请求头。
        await LoginAsync(client, ct, AdminPassword);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        using var read = await client.GetAsync(route, ct);
        using var missingCsrf = await client.PutAsJsonAsync(route, settings, ct);

        // Then 读取仍可用；保存必须带上有效的 CSRF token。
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var problem = await AssertProblemAsync(missingCsrf, HttpStatusCode.BadRequest, ct);
        Assert.Contains("CSRF", problem.GetProperty("detail").GetString());
        await RefreshCsrfAsync(client, ct);
        using var save = await client.PutAsJsonAsync(route, settings, ct);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
    }

    [Fact]
    public async Task Profile_is_persisted_and_logout_revokes_only_the_current_device()
    {
        // Given 两个设备分别以管理员凭证登录。
        var ct = TestContext.Current.CancellationToken;
        using var first = fixture.CreateApiClient();
        using var second = fixture.CreateApiClient();
        await LoginAsync(first, ct, AdminPassword);
        await LoginAsync(second, ct, AdminPassword);

        // When 一个设备修改资料并退出。
        using var invalid = await first.PutAsJsonAsync("/api/account/profile",
            new { name = "", phone = new string('1', 21), email = "invalid" }, ct);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, ct);
        foreach (var invalidEmail in new[] { "a b@example.com", "a@@example.com" })
        {
            using var invalidAddress = await first.PutAsJsonAsync("/api/account/profile",
                new { name = "测试管理员", email = invalidEmail }, ct);
            var problem = await AssertProblemAsync(invalidAddress, HttpStatusCode.BadRequest, ct);
            Assert.True(problem.GetProperty("errors").TryGetProperty("email", out _));
        }

        using var profile = await first.PutAsJsonAsync("/api/account/profile",
            new { name = "测试管理员", phone = "123456", email = "user@example.com" }, ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var credentials = first.DefaultRequestHeaders.GetValues("Cookie").Single();
        using var logout = await first.PostAsync("/api/auth/logout", null, ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Then 另一设备看到新资料，重新发送已退出设备的 cookie 仍返回 401。
        using var replay = fixture.CreateApiClient();
        replay.DefaultRequestHeaders.Add("Cookie", credentials);
        using var rejected = await replay.GetAsync("/api/auth/me", ct);
        await AssertProblemAsync(rejected, HttpStatusCode.Unauthorized, ct);
        var me = await second.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.Equal("admin", me.GetProperty("username").GetString());
        Assert.Equal("测试管理员", me.GetProperty("name").GetString());
        Assert.Equal("123456", me.GetProperty("phone").GetString());
        Assert.Equal("user@example.com", me.GetProperty("email").GetString());
        using var clear = await second.PutAsJsonAsync("/api/account/profile",
            new { name = "管理员", phone = (string?)null, email = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
    }

    [Fact]
    public async Task Password_change_revokes_both_devices_and_any_concurrent_old_password_login()
    {
        // Given 两设备持有有效会话，第三设备准备使用旧密码并发登录。
        var ct = TestContext.Current.CancellationToken;
        using var first = fixture.CreateApiClient();
        using var second = fixture.CreateApiClient();
        using var racer = fixture.CreateApiClient();
        await LoginAsync(first, ct, AdminPassword);
        await LoginAsync(second, ct, AdminPassword);
        await RefreshCsrfAsync(racer, ct);
        const string newPassword = " new-password ";
        using var invalid = await first.PostAsJsonAsync("/api/auth/change-password",
            new { oldPassword = "wrong-password", newPassword }, ct);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, ct);
        using var missingOld = await first.PostAsJsonAsync("/api/auth/change-password", new { newPassword }, ct);
        await AssertProblemAsync(missingOld, HttpStatusCode.BadRequest, ct);

        // When 外部行锁使改密先进入锁队列，旧密码的登录随后也必须等待同一行。
        await using var observer = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await observer.OpenAsync(ct);
        await using var transaction = await observer.BeginTransactionAsync(ct);
        await using var lockAdmin = new NpgsqlCommand(
            """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
        Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

        var changeTask = first.PostAsJsonAsync("/api/auth/change-password",
            new { oldPassword = AdminPassword, newPassword }, ct);
        Task<HttpResponseMessage>? loginTask = null;
        try
        {
            await WaitForAdminLocksAsync(observer, 1, ct);
            loginTask = racer.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = AdminPassword }, ct);
            await WaitForAdminLocksAsync(observer, 2, ct);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        using var changed = await changeTask;
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        try
        {
            // Then 登录在持锁验证时只能看到新密码，两个已签发会话也都被撤销。
            using var raced = await loginTask!;
            await AssertProblemAsync(raced, HttpStatusCode.Unauthorized, ct);
            foreach (var client in new[] { first, second })
            {
                using var response = await client.GetAsync("/api/auth/me", ct);
                await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ct);
            }
        }
        finally
        {
            using var fresh = fixture.CreateApiClient();
            await LoginAsync(fresh, ct, newPassword);
            using var restored = await fresh.PostAsJsonAsync("/api/auth/change-password",
                new { oldPassword = newPassword, newPassword = AdminPassword }, ct);
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        }
    }

    internal static async Task WaitForAdminLocksAsync(NpgsqlConnection observer, long expected, CancellationToken ct, bool includeUpdates = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var blocked = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock'
              AND (query LIKE '%"AdminAccounts"%FOR UPDATE%'
                   OR (@includeUpdates AND query LIKE '%UPDATE "AdminAccounts"%'))
            """, observer);
        blocked.Parameters.AddWithValue("includeUpdates", includeUpdates);
        await using var clearSnapshot = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", observer);
        while ((long)(await blocked.ExecuteScalarAsync(timeout.Token))! != expected)
        {
            await clearSnapshot.ExecuteNonQueryAsync(timeout.Token);
            await Task.Delay(20, timeout.Token);
        }
    }

    [Fact]
    public async Task Legacy_restricted_session_and_antiforgery_token_survive_upgrade_until_database_expiry()
    {
        // Given 登录成功，cookie 为 Secure/HttpOnly/Lax，绝对有效期为 24 小时。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        var session = await LoginAsync(client, ct, AdminPassword);
        var loginAt = session.GetProperty("lastLoginAt").GetDateTimeOffset();
        var expiresAt = session.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(24), expiresAt - loginAt);

        // Given 旧架构里的账号必须改密，保留原数据库会话和浏览器保存的票据。
        var connectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261116092521_RequireInitialPasswordChange", ct);
            await db.Database.ExecuteSqlRawAsync("""UPDATE "AdminAccounts" SET "MustChangePassword" = TRUE""", ct);
        }

        // When API 重启并应用新迁移；Then 原会话可访问业务、读取和修改资料，无须重新登录。
        await fixture.RestartApiAsync(ct);
        using var business = await client.GetAsync("/api/ping", ct);
        Assert.Equal(HttpStatusCode.OK, business.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.Equal(expiresAt, me.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(me.TryGetProperty("mustChangePassword", out _));
        using var profile = await client.PutAsJsonAsync("/api/account/profile",
            new { name = "管理员", phone = (string?)null, email = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var profileJson = await profile.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(expiresAt, profileJson.GetProperty("expiresAt").GetDateTimeOffset());

        // Then PostgreSQL 会话到期后，客户端持有原 cookie 也返回 401。
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """UPDATE "AdminSessions" SET "ExpiresAt" = now() - interval '1 second'""", connection);
        Assert.True(await command.ExecuteNonQueryAsync(ct) > 0);
        using var expired = await client.GetAsync("/api/auth/me", ct);
        await AssertProblemAsync(expired, HttpStatusCode.Unauthorized, ct);
    }

    [Fact]
    public async Task Authentication_storage_failure_returns_503_and_health_remains_anonymous()
    {
        // Given 已登录的设备及隔离 PostgreSQL 中暂时无法查询的会话表。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await LoginAsync(client, ct, AdminPassword);
        await using var connection = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await connection.OpenAsync(ct);
        await using var rename = new NpgsqlCommand("""ALTER TABLE "AdminSessions" RENAME TO "UnavailableAdminSessions" """, connection);
        await rename.ExecuteNonQueryAsync(ct);
        try
        {
            // When 同一设备查询健康与受保护的业务。
            using var health = await client.GetAsync("/health", ct);
            using var ping = await client.GetAsync("/api/ping", ct);

            // Then 健康检查无需会话查询；业务报告服务暂不可用。
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            await AssertProblemAsync(ping, HttpStatusCode.ServiceUnavailable, ct);
        }
        finally
        {
            await using var restore = new NpgsqlCommand(
                """ALTER TABLE "UnavailableAdminSessions" RENAME TO "AdminSessions" """, connection);
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal static async Task<JsonElement> LoginAsync(HttpClient client, CancellationToken ct, string password)
    {
        await RefreshCsrfAsync(client, ct);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password }, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AddSessionCookie(client, login);
        await RefreshCsrfAsync(client, ct);
        return await login.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    internal static async Task RefreshCsrfAsync(HttpClient client, CancellationToken ct)
    {
        using var csrf = await client.GetAsync("/api/auth/csrf", ct);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (csrf.Headers.TryGetValues("Set-Cookie", out var cookies))
            AddCookie(client, cookies.Single().Split(';')[0]);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, body.GetProperty("requestToken").GetString());
    }

    private static void AddSessionCookie(HttpClient client, HttpResponseMessage login)
    {
        var cookie = login.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; expires=", cookie, StringComparison.OrdinalIgnoreCase);
        AddCookie(client, cookie.Split(';')[0]);
    }

    private static void AddCookie(HttpClient client, string cookie)
    {
        var previous = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
            ? values.Single() + "; " : "";
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", previous + cookie);
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken ct)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal((int)expected, body.GetProperty("status").GetInt32());
        return body;
    }
}
