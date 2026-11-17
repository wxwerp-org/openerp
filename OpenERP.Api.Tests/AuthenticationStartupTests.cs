using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenERP.Api.Authentication;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class AuthenticationStartupTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _database = $"openerp_auth_test_{Guid.NewGuid():N}";
    private string? _adminConnectionString;
    private string _connectionString = "";

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_database}" """, connection);
        await create.ExecuteNonQueryAsync(ct);
        _adminConnectionString = connectionString;
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = _database,
            Pooling = false
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnectionString is null)
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"""DROP DATABASE "{_database}" """, connection);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Production_requires_migrations_then_creates_one_uninitialized_account_without_an_initial_password()
    {
        // Given 独立空库和未配置管理员密码的 Production 进程。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var unmigrated = await RunApiAsync(null, ct);
        Assert.NotEqual(0, unmigrated.ExitCode);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync(ct));

        // When 部署侧应用数据库迁移，CLI 在缺少占位行时仍失败。
        await db.Database.MigrateAsync(ct);
        var missingSigningKey = await RunApiOnDatabaseAsync(_connectionString, null, [], ct, includeSigningKey: false);
        Assert.NotEqual(0, missingSigningKey.ExitCode);
        Assert.Contains("Authentication:SigningPrivateKeyPem", missingSigningKey.Output);

        var reset = await RunApiAsync(fixture.AdminPassword, ct, "reset-admin-password", "--stdin");
        Assert.Equal(1, reset.ExitCode);
        Assert.Contains("数据库中没有管理员账号", reset.Output);
        Assert.False(await db.AdminAccounts.AnyAsync(ct));

        // Then Production 无须初始密码，重复启动保留同一条未初始化占位。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var started = await RunApiOnDatabaseAsync(_connectionString, null, [], ct, stopWhenListening: true);
            Assert.True(started.Started, started.Output);
            var admin = await db.AdminAccounts.AsNoTracking().SingleAsync(ct);
            Assert.Equal(1, admin.Id);
            Assert.Equal("管理员", admin.Name);
            Assert.Null(admin.Phone);
            Assert.Null(admin.Email);
            Assert.Null(admin.PasswordHash);
        }

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash") VALUES (2, 'test', 'unused')""", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrade_preserves_existing_account_and_replaces_the_legacy_session_table(bool hadRequiredPasswordChange)
    {
        // Given 旧数据库中的两种账户状态均已保存密码、资料和旧会话记录。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.GetService<IMigrator>().MigrateAsync("20261116092521_RequireInitialPasswordChange", ct);
        var hash = new PasswordHasher<AdminAccount>().HashPassword(new AdminAccount(), fixture.AdminPassword);
        var now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        var expiresAt = now.AddHours(24);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdminAccounts" ("Id", "Name", "Phone", "Email", "PasswordHash", "LastLoginAt", "MustChangePassword")
            VALUES (1, '现有管理员', '123456', 'existing@example.com', {hash}, {now}, {hadRequiredPasswordChange});
            INSERT INTO "AdminSessions" ("Id", "AdminId", "ExpiresAt")
            VALUES ({first}, 1, {expiresAt}), ({second}, 1, {expiresAt.AddMinutes(1)});
            """, ct);

        // When 应用迁移；Then 移除旧列和旧会话表，保留密码与资料。
        await db.Database.MigrateAsync(ct);
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'AdminAccounts' AND column_name = 'MustChangePassword') AS "Value"
            """).SingleAsync(ct));
        var existing = await db.AdminAccounts.AsNoTracking().SingleAsync(ct);
        Assert.Equal("现有管理员", existing.Name);
        Assert.Equal("123456", existing.Phone);
        Assert.Equal("existing@example.com", existing.Email);
        Assert.Equal(hash, existing.PasswordHash);
        Assert.Equal(now, existing.LastLoginAt);
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = 'AdminSessions') AS "Value"
            """).SingleAsync(ct));
        Assert.Empty(await db.RefreshTokens.AsNoTracking().ToListAsync(ct));

        var store = CreateStore(db, new TestClock { UtcNow = now });
        var login = await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct);
        Assert.NotNull(login);
        Assert.Equal(now.AddHours(24), login.ExpiresAt);
    }

    [Fact]
    public async Task Cli_reset_reads_stdin_updates_admin_and_revokes_all_refresh_tokens_without_signing_key()
    {
        // Given 已初始化的数据库、两个刷新令牌以及不带 signing key、Redis、S3 配置的 CLI 进程。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        var store = CreateStore(db, TimeProvider.System);
        await store.SeedAsync(ct);
        Assert.True(await store.InitializeAsync(fixture.AdminPassword, ct));
        var one = await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct);
        var two = await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct);
        Assert.NotNull(one);
        Assert.NotNull(two);

        // When 传递额外 argv 被拒绝，显式 --stdin 重置成功。
        var invalid = await RunApiAsync(null, ct, "reset-admin-password", "unused-argument");
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.NotNull(await store.FindActiveRefreshTokenAsync(one.Id, ct));
        const string replacement = " replacement ";
        await using var observer = new NpgsqlConnection(_connectionString);
        await observer.OpenAsync(ct);
        await using var transaction = await observer.BeginTransactionAsync(ct);
        await using var lockAdmin = new NpgsqlCommand(
            """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
        Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

        var reset = RunApiAsync(replacement, ct, "reset-admin-password", "--stdin");
        Task<AdminRefreshToken?>? login = null;
        try
        {
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 1, ct);
            login = store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct);
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 2, ct);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        var result = await reset;

        // Then 两个旧刷新令牌和旧密码均失效，新密码保留首尾空白且能登录。
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(replacement, result.Output);
        Assert.Null(await login!);
        Assert.Null(await store.FindActiveRefreshTokenAsync(one.Id, ct));
        Assert.Null(await store.FindActiveRefreshTokenAsync(two.Id, ct));
        Assert.Null(await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct));
        var replacementToken = await store.LoginAsync("admin", replacement, RandomNumberGenerator.GetBytes(32), ct);
        Assert.NotNull(replacementToken);
    }

    [Fact]
    public async Task Refresh_token_expires_at_the_exact_deadline_and_next_login_prunes_it()
    {
        // Given 使用可控制时间的时钟签发一个数据库刷新令牌。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero) };
        var store = CreateStore(db, clock);
        await store.SeedAsync(ct);
        Assert.True(await store.InitializeAsync(fixture.AdminPassword, ct));
        var token = await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct);
        Assert.NotNull(token);
        Assert.Equal(clock.UtcNow.AddHours(24), token.ExpiresAt);

        // When 到期前一微秒和恰好到期时查询。
        clock.UtcNow = token.ExpiresAt.AddTicks(-10);
        Assert.NotNull(await store.FindActiveRefreshTokenAsync(token.Id, ct));
        clock.UtcNow = token.ExpiresAt;

        // Then 恰好到期即失效，后续登录清理已过期记录。
        Assert.Null(await store.FindActiveRefreshTokenAsync(token.Id, ct));
        Assert.NotNull(await store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), ct));
        Assert.False(await db.RefreshTokens.AnyAsync(current => current.Id == token.Id, ct));
    }

    private AppDbContext CreateContext() => new(CreateOptions());

    private DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options;

    private AccountStore CreateStore(AppDbContext db, TimeProvider clock) =>
        new(db, CreateOptions(), new PasswordHasher<AdminAccount>(), clock);

    private Task<(int ExitCode, string Output, bool Started)> RunApiAsync(string? password, CancellationToken ct, params string[] args) =>
        RunApiOnDatabaseAsync(_connectionString, password, args, ct);

    internal static async Task<(int ExitCode, string Output, bool Started)> RunApiOnDatabaseAsync(
        string connectionString, string? password, string[] args, CancellationToken ct,
        bool stopWhenListening = false, bool includeSigningKey = true)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(AppDbContext).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment.Remove("Authentication__InitialPassword");
        start.Environment.Remove("Authentication__SigningPrivateKeyPem");
        start.Environment["ConnectionStrings__openerp"] = connectionString;
        start.Environment.Remove("ConnectionStrings__redis");
        start.Environment.Remove("ConnectionStrings__seaweedfs");
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        if (args.Length == 0)
        {
            if (includeSigningKey)
            {
                using var rsa = RSA.Create(2048);
                start.Environment["Authentication__SigningPrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem();
            }

            // Production 启动测试仅验证启动和数据库，不调用 Redis 或文件存储。
            start.Environment["ConnectionStrings__redis"] = "localhost:1";
            start.Environment["ConnectionStrings__seaweedfs"] = "Endpoint=http://127.0.0.1:1;AccessKey=test;SecretKey=test";
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动测试用 API 进程。");
        if (password is not null)
            await process.StandardInput.WriteLineAsync(password.AsMemory(), ct);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        var stdout = new System.Text.StringBuilder();
        var started = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                stdout.AppendLine(line);
                if (stopWhenListening && line.Contains("Now listening on:", StringComparison.Ordinal))
                {
                    started = true;
                    process.Kill(entireProcessTree: true);
                    break;
                }
            }
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return (process.ExitCode, stdout.ToString() + await stderr, started);
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
