using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FirstLoginIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Fresh_database_reports_initialization_required_without_creating_a_session()
    {
        // Given 独立空库启动完成，匿名设备无登录 cookie。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture(initializeAdmin: false);
        await fixture.InitializeAsync();
        using var client = fixture.CreateApiClient();

        // When 查询管理员状态、访问业务或尝试登录。
        await AssertStatusAsync(client, false, ct);
        await AssertCodeAsync(await client.GetAsync("/api/ping", ct), "initialization_required", ct);
        await AuthenticationIntegrationTests.RefreshCsrfAsync(client, ct);
        await AssertCodeAsync(await AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(client, "/api/auth/login",
            new { username = "admin", password = fixture.AdminPassword }, ct), "initialization_required", ct);

        // Given 数据库账号缺失或查询故障；Then 初始化入口和匿名业务拒绝继续。
        await using var connection = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await connection.OpenAsync(ct);
        await using (var missing = new NpgsqlCommand("""DELETE FROM "AdminAccounts" """, connection))
            Assert.Equal(1, await missing.ExecuteNonQueryAsync(ct));
        await AssertUnavailableAsync(client, ct);
        await using var broken = new NpgsqlCommand("""ALTER TABLE "AdminAccounts" RENAME TO "UnavailableAdminAccounts" """, connection);
        await broken.ExecuteNonQueryAsync(ct);
        await AssertUnavailableAsync(client, ct);
    }

    [Fact]
    public async Task Initialization_rolls_back_on_database_failure_and_two_connections_have_one_winner()
    {
        // Given 两个匿名会话和独立的未初始化数据库。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture(initializeAdmin: false);
        await fixture.InitializeAsync();
        using var first = fixture.CreateApiClient();
        using var second = fixture.CreateApiClient();
        const string firstPassword = " abcd ";
        const string secondPassword = " 123456789012345678 ";
        const string forwardedAddress = "203.0.113.87";
        first.DefaultRequestHeaders.UserAgent.ParseAdd("OpenERP-Test-First/1.0");
        second.DefaultRequestHeaders.UserAgent.ParseAdd("OpenERP-Test-Second/1.0");
        first.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedAddress);
        second.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedAddress);
        var earliestInitialization = DateTimeOffset.UtcNow;

        // When 未提供 CSRF 或密码长度越界；Then 拒绝且仍未初始化。
        using var noToken = await InitializeAsync(first, firstPassword, ct);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await AuthenticationIntegrationTests.RefreshCsrfAsync(first, ct);
        await AuthenticationIntegrationTests.RefreshCsrfAsync(second, ct);
        foreach (var password in new[] { null, "", "12345", new string('1', 21) })
        {
            using var invalid = await InitializeAsync(first, password, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        await using var observer = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await observer.OpenAsync(ct);
        // When 延迟触发器使 UPDATE 在提交时回滚；Then 状态仍为未初始化。
        await using (var fail = new NpgsqlCommand("""
            CREATE FUNCTION fail_admin_initialization() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'test transaction rollback'; END;
            $$;
            CREATE CONSTRAINT TRIGGER fail_admin_initialization AFTER UPDATE ON "AdminAccounts"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION fail_admin_initialization();
            """, observer))
            await fail.ExecuteNonQueryAsync(ct);
        using var rejected = await InitializeAsync(first, firstPassword, ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        await AssertStatusAsync(first, false, ct);
        await using (var recover = new NpgsqlCommand(
            """DROP TRIGGER fail_admin_initialization ON "AdminAccounts" """, observer))
            await recover.ExecuteNonQueryAsync(ct);

        // When 数据库行锁释放前两个请求已在不同连接等待；Then 只有一方成功。
        await using (var transaction = await observer.BeginTransactionAsync(ct))
        {
            await using var lockAdmin = new NpgsqlCommand(
                """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
            Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));
            var firstAttempt = InitializeAsync(first, firstPassword, ct);
            var secondAttempt = InitializeAsync(second, secondPassword, ct);
            try
            {
                await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 2, ct, includeUpdates: true);
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            using var firstResult = await firstAttempt;
            using var secondResult = await secondAttempt;
            var winner = Assert.Single(new[] { firstResult, secondResult }, result => result.StatusCode == HttpStatusCode.NoContent);
            var loser = winner == firstResult ? secondResult : firstResult;
            Assert.False(winner.Headers.Contains("Set-Cookie"));
            await AssertCodeAsync(loser, "already_initialized", ct);
            var winningPassword = winner == firstResult ? firstPassword : secondPassword;
            var losingPassword = winner == firstResult ? secondPassword : firstPassword;

            await AssertStatusAsync(first, true, ct);
            await AssertCodeAsync(await InitializeAsync(first, losingPassword, ct), "already_initialized", ct);
            await using var refreshTokens = new NpgsqlCommand("""SELECT COUNT(*) FROM "RefreshTokens" """, observer);
            Assert.Equal(0L, await refreshTokens.ExecuteScalarAsync(ct));
            using var anonymous = await first.GetAsync("/api/ping", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // Then 仅成功提交的请求记录时间、实际连接 IP 和获胜设备的 User-Agent。
            var log = Assert.Single(await fixture.GetApiLogLinesAsync("管理员初始化成功", ct));
            var fields = Regex.Match(log, @"InitializedAtUtc=(\S+) RemoteIpAddress=(\S+) UserAgent=(\S+)");
            Assert.True(fields.Success, log);
            var initializedAt = DateTimeOffset.ParseExact(fields.Groups[1].Value, "O", null);
            Assert.Equal(TimeSpan.Zero, initializedAt.Offset);
            Assert.InRange(initializedAt, earliestInitialization, DateTimeOffset.UtcNow);
            Assert.True(IPAddress.TryParse(fields.Groups[2].Value, out var address));
            Assert.NotEqual(IPAddress.Parse(forwardedAddress), address);
            Assert.Equal(winner == firstResult ? "OpenERP-Test-First/1.0" : "OpenERP-Test-Second/1.0", fields.Groups[3].Value);
            Assert.DoesNotContain(firstPassword, log);
            Assert.DoesNotContain(secondPassword, log);
            output.WriteLine(log);

            // When API 重启；Then 初始化保留，且只有获胜原始密码可以登录。
            await fixture.RestartApiAsync(ct);
            await AssertStatusAsync(first, true, ct);
            foreach (var wrongPassword in new[] { losingPassword, winningPassword.Trim() })
            {
                using var invalidLogin = await AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(first,
                    "/api/auth/login", new { username = "admin", password = wrongPassword }, ct);
                Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);
            }

            var profile = await AuthenticationIntegrationTests.LoginAsync(first, ct, winningPassword);
            Assert.False(profile.TryGetProperty("mustChangePassword", out _));
            using var ping = await first.GetAsync("/api/ping", ct);
            Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        }
    }

    [Fact]
    public async Task Password_reset_can_initialize_a_placeholder_before_a_competing_browser()
    {
        // Given 独立空库已创建占位，管理命令与浏览器准备设置不同的新密码。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture(initializeAdmin: false);
        await fixture.InitializeAsync();
        using var browser = fixture.CreateApiClient();
        await AssertStatusAsync(browser, false, ct);
        await AuthenticationIntegrationTests.RefreshCsrfAsync(browser, ct);
        var connectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(ct);
        await using var transaction = await observer.BeginTransactionAsync(ct);
        await using var lockAdmin = new NpgsqlCommand(
            """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
        Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));
        // 模拟残留会话，验证 CLI 对占位账号设置密码时同样撤销全部会话。
        await using var insert = new NpgsqlCommand("""
            INSERT INTO "RefreshTokens" ("Id", "AdminId", "TokenHash", "ExpiresAt")
            VALUES (gen_random_uuid(), 1, @tokenHash, now() + interval '1 hour')
            """, observer, transaction);
        insert.Parameters.AddWithValue("tokenHash", RandomNumberGenerator.GetBytes(32));
        Assert.Equal(1, await insert.ExecuteNonQueryAsync(ct));

        // When CLI 先排入同一行的锁队列，浏览器请求随后等待。
        const string resetPassword = " reset-password ";
        var reset = AuthenticationStartupTests.RunApiOnDatabaseAsync(
            connectionString, resetPassword, ["reset-admin-password", "--stdin"], ct);
        Task<HttpResponseMessage>? initialize = null;
        try
        {
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 1, ct);
            initialize = InitializeAsync(browser, "browser-password", ct);
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 2, ct, includeUpdates: true);
        }
        finally
        {
            await transaction.CommitAsync(CancellationToken.None);
        }

        // Then CLI 成功设置最终密码并撤销残留会话，浏览器不能覆盖。
        var result = await reset;
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(resetPassword, result.Output);
        await AssertCodeAsync(await initialize!, "already_initialized", ct);
        await using var refreshTokens = new NpgsqlCommand("""SELECT COUNT(*) FROM "RefreshTokens" """, observer);
        Assert.Equal(0L, await refreshTokens.ExecuteScalarAsync(ct));
        await AssertStatusAsync(browser, true, ct);
        await AuthenticationIntegrationTests.LoginAsync(browser, ct, resetPassword);
    }

    private static Task<HttpResponseMessage> InitializeAsync(HttpClient client, string? password, CancellationToken ct) =>
        AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(
            client, "/api/auth/initialize", new { newPassword = password }, ct);

    private static async Task AssertUnavailableAsync(HttpClient client, CancellationToken ct)
    {
        using var status = await client.GetAsync("/api/auth/status", ct);
        using var business = await client.GetAsync("/api/ping", ct);
        using var initialize = await InitializeAsync(client, "test-password", ct);
        using var login = await AuthenticationIntegrationTests.PostJsonAnonymouslyAsync(
            client, "/api/auth/login", new { username = "admin", password = "test-password" }, ct);
        Assert.All(new[] { status, business, initialize, login }, response =>
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        });
    }

    private static async Task AssertStatusAsync(HttpClient client, bool initialized, CancellationToken ct)
    {
        using var response = await client.GetAsync("/api/auth/status", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(initialized, body.GetProperty("initialized").GetBoolean());
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, string code, CancellationToken ct)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(code, body.GetProperty("code").GetString());
        }
    }
}
