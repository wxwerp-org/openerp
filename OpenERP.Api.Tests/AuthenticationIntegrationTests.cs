using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class AuthenticationIntegrationTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await fixture.EnsureApiStartedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string AdminPassword => fixture.AdminPassword;
    internal const string CsrfHeader = "X-CSRF-TOKEN";
    private const string CsrfCookieName = "__Host-OpenERP.Csrf";
    internal const string RefreshCookieName = "__Secure-OpenERP.Refresh";

    [Fact]
    public async Task Health_and_csrf_are_anonymous_but_business_and_mutations_are_protected()
    {
        // Given 未登录且没有 CSRF token 的独立浏览器。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();

        // When 查询健康状态和业务，并尝试登录。
        using var health = await client.GetAsync("/health", ct);
        using var alive = await client.GetAsync("/alive", ct);
        using var orders = await client.GetAsync("/api/orders", ct);
        using var me = await client.GetAsync("/api/auth/me", ct);
        using var missingToken = await PostJsonAnonymouslyAsync(client, "/api/auth/login",
            new { username = "admin", password = AdminPassword }, ct);

        // Then 健康和 CSRF 正常，业务返回 401，缺少 CSRF 的登录返回 400。
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        await AssertProblemAsync(orders, HttpStatusCode.Unauthorized, ct);
        await AssertProblemAsync(me, HttpStatusCode.Unauthorized, ct);
        await AssertProblemAsync(missingToken, HttpStatusCode.BadRequest, ct);

        await RefreshCsrfAsync(client, ct);
        using var badPassword = await PostJsonAnonymouslyAsync(client, "/api/auth/login",
            new { username = "admin", password = AdminPassword.Trim() }, ct);
        await AssertProblemAsync(badPassword, HttpStatusCode.Unauthorized, ct);

        await LoginAsync(client, ct, AdminPassword);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        using var file = new MultipartFormDataContent { { new StringContent("file"), "file", "file.png" } };
        using var upload = await client.PostAsync("/api/files/", file, ct);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var key = Assert.IsType<string>(uploaded.GetProperty("key").GetString());
        Assert.False(string.IsNullOrWhiteSpace(key));
        using var delete = await client.DeleteAsync($"/api/files/{Uri.EscapeDataString(key)}", ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task About_is_anonymous_and_does_not_renew_the_session()
    {
        // Given 已初始化且未登录的独立浏览器。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();

        // When 匿名请求只读版本信息。
        using var response = await client.GetAsync("/api/about", ct);

        // Then 返回 200 和三个字段，且不下发 Cookie（不触发会话续期）。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(["version", "commit", "buildTime"],
            body.EnumerateObject().Select(property => property.Name));
        var version = body.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public async Task Removed_ping_endpoint_returns_not_found()
    {
        // Given 管理员已登录。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await LoginAsync(client, ct, AdminPassword);

        // When 请求已删除的 /api/ping。
        using var response = await client.GetAsync("/api/ping", ct);

        // Then 路由不再存在，返回 404。
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_sync_settings_require_a_ready_bearer_but_not_csrf()
    {
        // Given 独立浏览器尚未登录，准备提交有效的关闭设置。
        const string route = "/api/order-sync/settings";
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
        using var save = await client.PutAsJsonAsync(route, settings, ct);

        // Then Bearer 可读取和保存业务设置，无须 CSRF token。
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
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

        // When 一个设备以 Bearer 修改资料但不提供 CSRF，再以 refresh Cookie 退出。
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

        first.DefaultRequestHeaders.Remove(CsrfHeader);
        using var profile = await first.PutAsJsonAsync("/api/account/profile",
            new { name = "测试管理员", phone = "123456", email = "user@example.com" }, ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var missingLogoutCsrf = await PostAnonymouslyAsync(first, "/api/auth/logout", null, ct);
        await AssertProblemAsync(missingLogoutCsrf, HttpStatusCode.BadRequest, ct);
        await RefreshCsrfAsync(first, ct);
        using var logout = await PostAnonymouslyAsync(first, "/api/auth/logout", null, ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Empty(await logout.Content.ReadAsByteArrayAsync(ct));

        // Then 另一设备的刷新令牌仍有效、未轮换且不改变最近登录时间，并能继续访问新资料。
        var originalRefreshCookie = GetCookiePair(second, RefreshCookieName);
        second.DefaultRequestHeaders.Remove(CsrfHeader);
        using var missingRefreshCsrf = await PostAnonymouslyAsync(second, "/api/auth/refresh", null, ct);
        await AssertProblemAsync(missingRefreshCsrf, HttpStatusCode.BadRequest, ct);
        await RefreshCsrfAsync(second, ct);
        using var refreshed = await PostAnonymouslyAsync(second, "/api/auth/refresh", null, ct);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.False(refreshed.Headers.Contains("Set-Cookie"));
        var refreshedBody = await refreshed.Content.ReadFromJsonAsync<JsonElement>(ct);
        var refreshedAccount = refreshedBody.GetProperty("account");
        var secondAccount = await second.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.Equal(secondAccount.GetProperty("lastLoginAt").GetDateTimeOffset(), refreshedAccount.GetProperty("lastLoginAt").GetDateTimeOffset());
        Assert.Equal(secondAccount.GetProperty("expiresAt").GetDateTimeOffset(), refreshedAccount.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(originalRefreshCookie, GetCookiePair(second, RefreshCookieName));
        var refreshedAccessToken = Assert.IsType<string>(refreshedBody.GetProperty("accessToken").GetString());
        Assert.False(string.IsNullOrWhiteSpace(refreshedAccessToken));
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAccessToken);

        var me = await second.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.Equal("admin", me.GetProperty("username").GetString());
        Assert.Equal("测试管理员", me.GetProperty("name").GetString());
        Assert.Equal("123456", me.GetProperty("phone").GetString());
        Assert.Equal("user@example.com", me.GetProperty("email").GetString());
        using var clear = await second.PutAsJsonAsync("/api/account/profile",
            new { name = "管理员", phone = (string?)null, email = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        // When 重放已退出设备的 Bearer 和 refresh Cookie；Then 两种凭证都失效。
        using var firstJwtReplay = fixture.CreateApiClient();
        firstJwtReplay.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        using var rejectedJwt = await firstJwtReplay.GetAsync("/api/auth/me", ct);
        await AssertProblemAsync(rejectedJwt, HttpStatusCode.Unauthorized, ct);

        using var refreshReplay = fixture.CreateApiClient();
        SetCookie(refreshReplay, GetCookiePair(first, RefreshCookieName));
        await RefreshCsrfAsync(refreshReplay, ct);
        using var rejectedRefresh = await PostAnonymouslyAsync(refreshReplay, "/api/auth/refresh", null, ct);
        await AssertProblemAsync(rejectedRefresh, HttpStatusCode.Unauthorized, ct);
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
        using var operations = new CancellationTokenSource();
        Exception? failure = null;
        try
        {
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
                """SELECT "Id" FROM "Users" WHERE "Username" = 'admin' FOR UPDATE""", observer, transaction);
            Assert.IsType<Guid>(await lockAdmin.ExecuteScalarAsync(ct));

            first.DefaultRequestHeaders.Remove(CsrfHeader);
            var changeTask = first.PostAsJsonAsync("/api/auth/change-password",
                new { oldPassword = AdminPassword, newPassword }, operations.Token);
            Task<HttpResponseMessage>? loginTask = null;
            Exception? coordinationFailure = null;
            try
            {
                await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "HTTP 改密进入锁队列",
                    [new("改密请求", changeTask, () => $"HTTP {(int)changeTask.Result.StatusCode}")]);
                loginTask = PostJsonAnonymouslyAsync(racer, "/api/auth/login",
                    new { username = "admin", password = AdminPassword }, operations.Token);
                await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 2, ct, "旧密码登录进入锁队列",
                    [new("改密请求", changeTask, () => $"HTTP {(int)changeTask.Result.StatusCode}"),
                     new("旧密码登录", loginTask, () => $"HTTP {(int)loginTask.Result.StatusCode}")]);
            }
            catch (Exception exception)
            {
                coordinationFailure = exception;
            }
            finally
            {
                await AuthenticationConcurrency.ReleaseAsync(transaction.RollbackAsync, operations,
                    coordinationFailure, ct, changeTask, loginTask);
            }

            using var changed = await changeTask;
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            // Then 登录在持锁验证时只能看到新密码，两个已签发会话也都被撤销。
            using var raced = await loginTask!;
            await AssertProblemAsync(raced, HttpStatusCode.Unauthorized, ct);
            foreach (var client in new[] { first, second })
            {
                using var response = await client.GetAsync("/api/auth/me", ct);
                await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ct);
            }
            using var changedPasswordClient = fixture.CreateApiClient();
            await LoginAsync(changedPasswordClient, ct, newPassword);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // 即使锁协调失败，请求仍可能已提交；使用独立预算确认并恢复共享账号。
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await RestoreAdminPasswordAsync(cleanup.Token);
        }
        catch (Exception exception)
        {
            if (failure is not null)
                throw new AggregateException("改密测试失败，且共享账号恢复失败。", failure, exception);
            throw;
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task RestoreAdminPasswordAsync(CancellationToken ct)
    {
        // CLI 在相同行锁内重置密码并撤销旧会话，晚到的原改密请求也不能再次改变密码。
        var connectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        var reset = await AuthenticationStartupTests.RunApiOnDatabaseAsync(connectionString, AdminPassword,
            ["reset-admin-password", "--stdin"], ct);
        Assert.Equal(0, reset.ExitCode);
        Assert.False(reset.Output.Contains(AdminPassword, StringComparison.Ordinal), "CLI 输出泄露了重置密码。");
        using var fresh = fixture.CreateApiClient();
        await LoginAsync(fresh, ct, AdminPassword);
    }

    [Fact]
    public async Task Legacy_authentication_migration_preserves_account_and_invalidates_old_tokens()
    {
        // Given 登录成功并留存旧 Bearer、refresh Cookie 和匿名 CSRF token。
        var ct = TestContext.Current.CancellationToken;
        using var beforeUpgrade = fixture.CreateApiClient();
        var account = await LoginAsync(beforeUpgrade, ct, AdminPassword);
        var loginAt = account.GetProperty("lastLoginAt").GetDateTimeOffset();
        var expiresAt = account.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(24), expiresAt - loginAt);
        var oldAccessToken = Assert.IsType<string>(beforeUpgrade.DefaultRequestHeaders.Authorization?.Parameter);
        var oldRefreshCookie = GetCookiePair(beforeUpgrade, RefreshCookieName);
        var oldCsrfCookie = GetCookiePair(beforeUpgrade, CsrfCookieName);
        var oldCsrfToken = beforeUpgrade.DefaultRequestHeaders.GetValues(CsrfHeader).Single();

        // Given 在独立旧版数据库中保留管理员资料、旧会话和用于验证匿名 CSRF 的密钥。
        var sourceConnectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(sourceConnectionString).Options);
        var admin = await source.Users.AsNoTracking().SingleAsync(user => user.Username == "admin", ct);
        var keys = await source.DataProtectionKeys.AsNoTracking().ToListAsync(ct);
        Assert.NotEmpty(keys);

        await using var legacyFixture = new AspireIntegrationFixture(initializeAdmin: false);
        await legacyFixture.InitializeAsync();
        var connectionString = await legacyFixture.GetPostgresConnectionStringAsync(ct);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261116092521_RequireInitialPasswordChange", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AdminAccounts" ("Id", "Name", "Phone", "Email", "PasswordHash", "LastLoginAt", "MustChangePassword")
                VALUES (1, {admin.Name}, {admin.Phone}, {admin.Email}, {admin.PasswordHash}, {loginAt}, TRUE);
                INSERT INTO "AdminSessions" ("Id", "AdminId", "ExpiresAt") VALUES ({Guid.NewGuid()}, 1, {expiresAt});
                """, ct);
            foreach (var key in keys)
                db.DataProtectionKeys.Add(new() { FriendlyName = key.FriendlyName, Xml = key.Xml });
            await db.SaveChangesAsync(ct);
        }

        // When 新版 API 启动并应用迁移；Then 账户资料保留，旧会话表和旧令牌失效。
        await legacyFixture.EnsureApiStartedAsync(ct);
        using var client = legacyFixture.CreateApiClient();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options))
        {
            Assert.False(await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_name = 'AdminSessions') AS "Value"
                """).SingleAsync(ct));
            Assert.Empty(await db.RefreshTokens.AsNoTracking().ToListAsync(ct));
            var existing = await db.Users.AsNoTracking().SingleAsync(user => user.Username == "admin", ct);
            Assert.Equal("管理员", existing.Name);
            Assert.Equal(loginAt, existing.LastLoginAt);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldAccessToken);
        using var expiredJwt = await client.GetAsync("/api/auth/me", ct);
        await AssertProblemAsync(expiredJwt, HttpStatusCode.Unauthorized, ct);

        // Then 旧匿名 CSRF token 仍被接受；旧 refresh Cookie 因迁移删除而返回 401。
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("Cookie");
        SetCookie(client, oldCsrfCookie);
        SetCookie(client, oldRefreshCookie);
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, oldCsrfToken);
        using var expiredRefresh = await client.PostAsync("/api/auth/refresh", null, ct);
        await AssertProblemAsync(expiredRefresh, HttpStatusCode.Unauthorized, ct);

        // When 用保留的旧密码重新登录；Then 新 Bearer 可读取和更新资料，旧限制字段已删除。
        var newAccount = await LoginAsync(client, ct, AdminPassword);
        Assert.Equal("admin", newAccount.GetProperty("username").GetString());
        Assert.False(newAccount.TryGetProperty("mustChangePassword", out _));
        Assert.Equal("管理员", newAccount.GetProperty("name").GetString());
        var newExpiresAt = newAccount.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(24), newExpiresAt - newAccount.GetProperty("lastLoginAt").GetDateTimeOffset());
        using var business = await client.GetAsync("/api/orders", ct);
        Assert.Equal(HttpStatusCode.OK, business.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", ct);
        Assert.Equal(newExpiresAt, me.GetProperty("expiresAt").GetDateTimeOffset());
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        using var profile = await client.PutAsJsonAsync("/api/account/profile",
            new { name = "管理员", phone = (string?)null, email = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var profileJson = await profile.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(newExpiresAt, profileJson.GetProperty("expiresAt").GetDateTimeOffset());

        // Then refresh 记录到期后，数据库校验拒绝其 Bearer。
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            UPDATE "RefreshTokens" SET "ExpiresAt" = now() - interval '1 second'
            WHERE "DeletedAt" IS NULL AND "UserId" = (SELECT "Id" FROM "Users" WHERE "Username" = 'admin')
            """, connection);
        Assert.True(await command.ExecuteNonQueryAsync(ct) > 0);
        using var expired = await client.GetAsync("/api/auth/me", ct);
        await AssertProblemAsync(expired, HttpStatusCode.Unauthorized, ct);

    }

    [Fact]
    public async Task Authentication_storage_failure_returns_503_and_health_remains_anonymous()
    {
        // Given 已登录的设备及隔离 PostgreSQL 中暂时无法查询的刷新令牌表。
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateApiClient();
        await LoginAsync(client, ct, AdminPassword);
        await using var connection = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await connection.OpenAsync(ct);
        await using var rename = new NpgsqlCommand("""ALTER TABLE "RefreshTokens" RENAME TO "UnavailableRefreshTokens" """, connection);
        await rename.ExecuteNonQueryAsync(ct);
        try
        {
            // When 同一设备查询健康与受保护的业务。
            using var health = await client.GetAsync("/health", ct);
            using var orders = await client.GetAsync("/api/orders", ct);
            using var refresh = await PostAnonymouslyAsync(client, "/api/auth/refresh", null, ct);

            // Then 健康检查无需会话查询；业务报告服务暂不可用。
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            await AssertProblemAsync(orders, HttpStatusCode.ServiceUnavailable, ct);
            await AssertProblemAsync(refresh, HttpStatusCode.ServiceUnavailable, ct);
        }
        finally
        {
            await using var restore = new NpgsqlCommand(
                """ALTER TABLE "UnavailableRefreshTokens" RENAME TO "RefreshTokens" """, connection);
            await restore.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // Then 存储恢复后，原刷新凭证仍能换发 JWT。
        using var recovered = await PostAnonymouslyAsync(client, "/api/auth/refresh", null, ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    internal static async Task<JsonElement> LoginAsync(HttpClient client, CancellationToken ct, string password, string username = "admin")
    {
        await RefreshCsrfAsync(client, ct);
        using var content = JsonContent.Create(new { username, password });
        using var login = await PostAnonymouslyAsync(client, "/api/auth/login", content, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
        var accessToken = Assert.IsType<string>(body.GetProperty("accessToken").GetString());
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        SetRefreshCookie(client, login);
        await RefreshCsrfAsync(client, ct);
        return body.GetProperty("account").Clone();
    }

    internal static async Task RefreshCsrfAsync(HttpClient client, CancellationToken ct)
    {
        var authorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync("/api/auth/csrf", ct);
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = authorization;
        }

        using var csrf = response;
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (csrf.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var cookie = Assert.Single(cookies);
            SetCookie(client, cookie.Split(';')[0]);
        }
        client.DefaultRequestHeaders.Remove(CsrfHeader);
        client.DefaultRequestHeaders.Add(CsrfHeader, body.GetProperty("requestToken").GetString());
    }

    internal static async Task<HttpResponseMessage> PostAnonymouslyAsync(
        HttpClient client, string requestUri, HttpContent? content, CancellationToken ct)
    {
        var authorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        try
        {
            return await client.PostAsync(requestUri, content, ct);
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = authorization;
        }
    }

    internal static async Task<HttpResponseMessage> PostJsonAnonymouslyAsync<T>(
        HttpClient client, string requestUri, T value, CancellationToken ct)
    {
        using var content = JsonContent.Create(value);
        return await PostAnonymouslyAsync(client, requestUri, content, ct);
    }

    private static void SetRefreshCookie(HttpClient client, HttpResponseMessage response)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; domain=", cookie, StringComparison.OrdinalIgnoreCase);
        SetCookie(client, cookie.Split(';')[0]);
    }

    private static string GetCookiePair(HttpClient client, string name)
    {
        var cookieHeader = Assert.Single(client.DefaultRequestHeaders.GetValues("Cookie"));
        return Assert.Single(cookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            pair => pair.StartsWith($"{name}=", StringComparison.Ordinal));
    }

    private static void SetCookie(HttpClient client, string cookie)
    {
        var name = cookie.Split('=', 2)[0];
        var cookies = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
            ? values.Single().Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();
        cookies.RemoveAll(value => string.Equals(value.Split('=', 2)[0], name, StringComparison.Ordinal));
        cookies.Add(cookie);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
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
