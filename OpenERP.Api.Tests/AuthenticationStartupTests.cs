using System.Diagnostics;
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
    public async Task Production_refuses_to_migrate_and_requires_the_seed_only_for_the_first_account()
    {
        // Given 独立的空数据库和未配置初始管理员密码的 Production 进程。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        // When 先尝试启动，再由部署侧应用迁移并再次启动。
        var unmigrated = await RunApiAsync(null, ct);
        Assert.NotEqual(0, unmigrated.ExitCode);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync(ct));
        await db.Database.MigrateAsync(ct);
        var unconfigured = await RunApiAsync(null, ct);

        // Then Production 不代为迁移，已迁移的空库仍明确缺少密码，重置也不能创建管理员。
        Assert.Equal(1, unconfigured.ExitCode);
        Assert.DoesNotContain("Unhandled exception", unconfigured.Output);
        Assert.Contains(AccountStore.InitialPasswordKey, unconfigured.Output);
        var reset = await RunApiAsync(fixture.AdminPassword, ct, "reset-admin-password", "--stdin");
        Assert.NotEqual(0, reset.ExitCode);
        Assert.Empty(await db.AdminAccounts.ToListAsync(ct));

        // Given 首次运行以合法 secret 创建管理员。
        var store = CreateStore(db, TimeProvider.System);
        await using var secondDb = CreateContext();
        var secondStore = CreateStore(secondDb, TimeProvider.System);
        var seeds = await Task.WhenAll(store.SeedAsync(fixture.AdminPassword, ct), secondStore.SeedAsync(fixture.AdminPassword, ct));
        Assert.All(seeds, seed => Assert.False(seed.IsErr));
        var original = await db.AdminAccounts.AsNoTracking().SingleAsync(ct);
        Assert.Equal("管理员", original.Name);
        Assert.Null(original.Phone);
        Assert.Null(original.Email);
        Assert.NotEqual(fixture.AdminPassword, original.PasswordHash);

        // When 新启动没有 secret 或配置了另一个值。
        Assert.False((await store.SeedAsync(null, ct)).IsErr);
        Assert.False((await store.SeedAsync("different-password", ct)).IsErr);

        // Then 配置变化不会覆盖既有密码，数据库约束最多允许一位管理员。
        var seeded = await db.AdminAccounts.AsNoTracking().SingleAsync(ct);
        Assert.Equal(original.PasswordHash, seeded.PasswordHash);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash") VALUES (2, 'test', 'unused')""", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
    }

    [Fact]
    public async Task Existing_admin_migration_keeps_password_and_does_not_require_a_change()
    {
        // Given 上一迁移已创建管理员，数据库中保存了现有密码与姓名。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.GetService<IMigrator>().MigrateAsync("20261116085427_CreateAdministratorAuthentication", ct);
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash") VALUES (1, '现有管理员', 'old-hash')""", ct);

        // When 数据库应用新迁移。
        await db.Database.MigrateAsync(ct);

        // Then 现有账户仍有原密码和姓名，也无需进行首次改密。
        var required = await db.Database.SqlQueryRaw<bool>(
            """SELECT "MustChangePassword" AS "Value" FROM "AdminAccounts" WHERE "Id" = 1""").SingleAsync(ct);
        Assert.False(required);
        var existing = await db.AdminAccounts.AsNoTracking().SingleAsync(ct);
        Assert.Equal("现有管理员", existing.Name);
        Assert.Equal("old-hash", existing.PasswordHash);
    }

    [Fact]
    public async Task Reset_reads_stdin_updates_existing_admin_and_revokes_all_sessions()
    {
        // Given 已初始化的数据库、两个会话以及不带 Redis/S3 配置的独立进程。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        var store = CreateStore(db, TimeProvider.System);
        Assert.False((await store.SeedAsync(fixture.AdminPassword, ct)).IsErr);
        var one = await store.LoginAsync("admin", fixture.AdminPassword, ct);
        var two = await store.LoginAsync("admin", fixture.AdminPassword, ct);
        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.True(one.Admin.MustChangePassword);

        // When 传递额外 argv 被拒绝，显式 --stdin 重置成功。
        var invalid = await RunApiAsync(null, ct, "reset-admin-password", "unused-argument");
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.NotNull(await store.FindSessionAsync(one.Id, ct));
        const string replacement = " replacement ";
        await using var observer = new NpgsqlConnection(_connectionString);
        await observer.OpenAsync(ct);
        await using var transaction = await observer.BeginTransactionAsync(ct);
        await using var lockAdmin = new NpgsqlCommand(
            """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
        Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

        var reset = RunApiAsync(replacement, ct, "reset-admin-password", "--stdin");
        Task<AdminSession?>? login = null;
        try
        {
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 1, ct);
            login = store.LoginAsync("admin", fixture.AdminPassword, ct);
            await AuthenticationIntegrationTests.WaitForAdminLocksAsync(observer, 2, ct);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        var result = await reset;

        // Then 两个旧会话和旧密码均失效，新密码保留首尾空白且能登录。
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(replacement, result.Output);
        Assert.Null(await login!);
        Assert.Null(await store.FindSessionAsync(one.Id, ct));
        Assert.Null(await store.FindSessionAsync(two.Id, ct));
        Assert.Null(await store.LoginAsync("admin", fixture.AdminPassword, ct));
        var replacementSession = await store.LoginAsync("admin", replacement, ct);
        Assert.NotNull(replacementSession);
        Assert.False(replacementSession.Admin.MustChangePassword);
    }

    [Fact]
    public async Task Session_is_expired_at_the_exact_deadline_and_next_login_prunes_it()
    {
        // Given 使用可控制时间的时钟签发一个数据库会话。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero) };
        var store = CreateStore(db, clock);
        Assert.False((await store.SeedAsync(fixture.AdminPassword, ct)).IsErr);
        var session = await store.LoginAsync("admin", fixture.AdminPassword, ct);
        Assert.NotNull(session);
        Assert.Equal(clock.UtcNow.AddHours(24), session.ExpiresAt);

        // When 到期前一微秒和恰好到期时查询。
        clock.UtcNow = session.ExpiresAt.AddTicks(-10);
        Assert.NotNull(await store.FindSessionAsync(session.Id, ct));
        clock.UtcNow = session.ExpiresAt;

        // Then 恰好到期即失效，后续登录清理已过期记录。
        Assert.Null(await store.FindSessionAsync(session.Id, ct));
        Assert.NotNull(await store.LoginAsync("admin", fixture.AdminPassword, ct));
        Assert.False(await db.AdminSessions.AnyAsync(current => current.Id == session.Id, ct));
    }

    private AppDbContext CreateContext() => new(CreateOptions());

    private DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options;

    private AccountStore CreateStore(AppDbContext db, TimeProvider clock) =>
        new(db, CreateOptions(), new PasswordHasher<AdminAccount>(), clock);

    private async Task<(int ExitCode, string Output)> RunApiAsync(string? password, CancellationToken ct, params string[] args)
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
        start.Environment["Authentication__InitialPassword"] = "";
        start.Environment["ConnectionStrings__openerp"] = _connectionString;
        start.Environment.Remove("ConnectionStrings__redis");
        start.Environment.Remove("ConnectionStrings__seaweedfs");
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        if (args.Length == 0)
        {
            // 只进入正常启动路径时提供占位连接；验证在启动任何服务前退出。
            start.Environment["ConnectionStrings__redis"] = "localhost:1";
            start.Environment["ConnectionStrings__seaweedfs"] = "Endpoint=http://127.0.0.1:1;AccessKey=test;SecretKey=test";
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动测试用 API 进程。");
        if (password is not null)
            await process.StandardInput.WriteLineAsync(password.AsMemory(), ct);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
