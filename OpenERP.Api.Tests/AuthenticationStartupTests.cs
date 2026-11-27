using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenERP.Api.Authentication;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed partial class AuthenticationStartupTests(AspireIntegrationFixture fixture) : IAsyncLifetime
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

        using var cleanup = new CancellationTokenSource(AuthenticationConcurrency.CleanupBudget);
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cleanup.Token);
        await using var drop = new NpgsqlCommand($"""DROP DATABASE "{_database}" """, connection);
        await drop.ExecuteNonQueryAsync(cleanup.Token);
        _adminConnectionString = null;
    }

    [Fact]
    public async Task Production_requires_migrations_then_creates_one_uninitialized_account_without_an_initial_password()
    {
        // Given 独立空库和未配置管理员密码或 JWT 签名密钥的 Production 进程。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var unmigrated = await RunApiAsync(null, ct);
        Assert.NotEqual(0, unmigrated.ExitCode);
        Assert.Contains(PostgresErrorCodes.UndefinedTable, unmigrated.Output);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync(ct));

        // When 部署侧应用数据库迁移，CLI 在缺少占位行时仍失败。
        await db.Database.MigrateAsync(ct);
        var reset = await RunApiAsync(fixture.AdminPassword, ct, "reset-admin-password", "--stdin");
        Assert.Equal(1, reset.ExitCode);
        Assert.True(reset.Output.Contains("数据库中没有管理员账号", StringComparison.Ordinal));
        Assert.False(await db.Users.AnyAsync(ct));

        // Then Production 无须初始密码或 JWT 签名密钥，重复启动保留同一条未初始化占位；
        // 文件和订单同步仍要求初始化；来自 HTTPS 反代的请求可以获取防伪令牌。
        var storage = await fixture.GetStorageConnectionStringAsync(ct);
        Guid? adminId = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var started = await RunApiOnDatabaseAsync(_connectionString, null, [], ct,
                configureStart: start => start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                storageConnectionString: storage, verifyListening: VerifyProductionRoutesAsync);
            Assert.True(started.Started, started.Output);
            var admin = await db.Users.AsNoTracking().SingleAsync(ct);
            Assert.NotEqual(Guid.Empty, admin.Id);
            adminId ??= admin.Id;
            Assert.Equal(adminId.Value, admin.Id);
            Assert.Equal(UserAccount.AdminUsername, admin.Username);
            Assert.Equal("管理员", admin.Name);
            Assert.Null(admin.Phone);
            Assert.Null(admin.Email);
            Assert.Null(admin.PasswordHash);
        }

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id", "Username", "Name", "PasswordHash")
            VALUES ({Guid.NewGuid()}, {UserAccount.AdminUsername}, 'test', 'unused')
            """, ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, rejected.SqlState);
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
        var hash = new PasswordHasher<UserAccount>().HashPassword(new UserAccount(), fixture.AdminPassword);
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
                WHERE table_name = 'Users' AND column_name = 'MustChangePassword') AS "Value"
            """).SingleAsync(ct));
        var existing = await db.Users.AsNoTracking().SingleAsync(ct);
        Assert.Equal(UserAccount.AdminUsername, existing.Username);
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
    public async Task Cli_reset_reads_stdin_updates_admin_and_revokes_all_refresh_tokens_without_redis_or_storage()
    {
        // Given 已初始化的数据库、两个刷新令牌以及未配置 Redis 或对象存储的 CLI 进程。
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
            """SELECT "Id" FROM "Users" WHERE "Username" = 'admin' FOR UPDATE""", observer, transaction);
        Assert.IsType<Guid>(await lockAdmin.ExecuteScalarAsync(ct));

        using var operations = new CancellationTokenSource();
        var reset = RunApiAsync(replacement, operations.Token, "reset-admin-password", "--stdin");
        var resetOperation = new BlockedOperation("重置密码 CLI", reset,
            () => $"退出码 {reset.Result.ExitCode}");
        Task<UserRefreshToken?>? login = null;
        Exception? failure = null;
        try
        {
            await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "等待 CLI 重置占用管理员锁",
                [resetOperation]);
            login = store.LoginAsync("admin", fixture.AdminPassword, RandomNumberGenerator.GetBytes(32), operations.Token);
            await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 2, ct, "等待 CLI 重置和旧密码登录",
                [resetOperation, new("旧密码登录", login)]);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            await AuthenticationConcurrency.ReleaseAsync(transaction.RollbackAsync, operations, failure, ct, reset, login);
        }

        var result = await reset;

        // Then 两个旧刷新令牌和旧密码均失效，新密码保留首尾空白且能登录。
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Output.Contains(replacement, StringComparison.Ordinal), "CLI 输出泄露了通过标准输入提交的密码。");
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
        new(db, CreateOptions(), new PasswordHasher<UserAccount>(), clock);

    private Task<(int ExitCode, string Output, bool Started)> RunApiAsync(string? password, CancellationToken ct, params string[] args) =>
        RunApiOnDatabaseAsync(_connectionString, password, args, ct);

    internal static async Task<(int ExitCode, string Output, bool Started)> RunApiOnDatabaseAsync(
        string connectionString, string? password, string[] args, CancellationToken ct,
        Action<ProcessStartInfo>? configureStart = null,
        TimeSpan? processBudget = null, string? storageConnectionString = null,
        Func<Uri, CancellationToken, Task>? verifyListening = null)
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
            // 只有完整启动场景需要真实的对象存储连接。
            start.Environment["ConnectionStrings__redis"] = "localhost:1";
            start.Environment["ConnectionStrings__seaweedfs"] = storageConnectionString
                ?? "Endpoint=http://127.0.0.1:1;AccessKey=test;SecretKey=test";
        }

        var secrets = new List<string?> { password, connectionString, storageConnectionString };
        secrets.Add(new NpgsqlConnectionStringBuilder(connectionString).Password);

        configureStart?.Invoke(start);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(processBudget ?? AuthenticationConcurrency.ProcessBudget);
        timeout.Token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动测试用 API 进程。");

        // 从启动时开始排空两个流，操作取消后继续读取，直到收尾关闭子进程的管道。
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = process.StandardInput;
        var inputPipe = input.BaseStream;
        var outputReader = process.StandardOutput;
        var errorReader = process.StandardError;
        using var drainCancellation = new CancellationTokenSource();
        var readStdout = ReadOutputAsync(outputReader, stdout, drainCancellation.Token,
            verifyListening is not null ? listening : null);
        var readStderr = ReadOutputAsync(errorReader, stderr, drainCancellation.Token);
        var readOutput = Task.WhenAll(readStdout, readStderr);
        var phase = "写入标准输入";
        Exception? failure = null;
        var cleanupFailures = new List<Exception>();
        var started = false;
        try
        {
            if (password is not null)
                await input.WriteLineAsync(password.AsMemory(), timeout.Token);
            await input.FlushAsync(timeout.Token);
            input.Close();

            phase = "等待子进程退出";
            if (verifyListening is not null)
            {
                phase = "等待 Production 监听";
                await Task.WhenAny(listening.Task, readStdout).WaitAsync(timeout.Token);
                started = listening.Task.IsCompletedSuccessfully;
                if (started)
                {
                    phase = "检查 Production HTTP 接口";
                    await verifyListening(await listening.Task, timeout.Token);
                }
            }

            if (!started)
                await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            // 进程收尾有独立 10 秒子预算，让上层事务协调仍能在 30 秒内观察其终态。
            using var cleanup = new CancellationTokenSource(AuthenticationConcurrency.ProcessCleanupBudget);
            if (failure is not null || started)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // 子进程在检查状态后自行退出。
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                }
            }

            try
            {
                await process.WaitForExitAsync(cleanup.Token);
            }
            catch (Exception error)
            {
                cleanupFailures.Add(error);
            }

            try
            {
                await readOutput.WaitAsync(cleanup.Token);
            }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested && !readOutput.IsCompleted)
            {
                cleanupFailures.Add(new TimeoutException("读取子进程输出超时。"));
                try
                {
                    await drainCancellation.CancelAsync();
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                }
            }
            catch
            {
                // 下方在关闭管道后统一观察并记录读取错误。
            }

            // Process 不负责释放调用方读取过的流。写入失败时直接关闭管道，避免再次写入密码。
            foreach (var pipe in new IDisposable[] { inputPipe, outputReader, errorReader })
            {
                try
                {
                    pipe.Dispose();
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                }
            }

            try
            {
                await readOutput;
            }
            catch (OperationCanceledException) when (drainCancellation.IsCancellationRequested)
            {
                // 已记录排空超时。读取任务已进入终态。
            }
            catch (Exception error)
            {
                if (readOutput.Exception is { } outputErrors)
                    cleanupFailures.AddRange(outputErrors.InnerExceptions);
                else
                    cleanupFailures.Add(error);
            }
        }

        var output = stdout.ToString() + stderr;
        // 使用 xUnit 原生输出通道，普通断言失败时同样能取得脱敏后的子进程日志。
        var diagnosticOutput = Redact(output);
        TestContext.Current.TestOutputHelper!.WriteLine(diagnosticOutput);
        if (failure is not null || cleanupFailures.Count > 0)
        {
            var reason = failure is OperationCanceledException && timeout.IsCancellationRequested
                ? ct.IsCancellationRequested ? "操作已取消" : "操作超时"
                : failure is null ? "读取子进程输出" : $"{failure.GetType().Name}：{Redact(failure.Message)}";
            var exitCode = process.HasExited ? process.ExitCode.ToString() : "未知";
            var cleanup = cleanupFailures.Count == 0 ? "" : "；收尾失败："
                + string.Join("；", cleanupFailures.Select(error => $"{error.GetType().Name}：{Redact(error.Message)}"));
            throw new InvalidOperationException($"{phase}失败（{reason}，退出码 {exitCode}）{cleanup}。\n{diagnosticOutput}");
        }

        return (process.ExitCode, output, started);

        string Redact(string value)
        {
            foreach (var secret in secrets.OfType<string>().Where(secret => secret.Length > 0).OrderByDescending(secret => secret.Length))
                value = value.Replace(secret, "[已隐藏]", StringComparison.Ordinal);
            return value;
        }
    }

    private static async Task ReadOutputAsync(StreamReader stream, StringBuilder output,
        CancellationToken ct, TaskCompletionSource<Uri>? listening = null)
    {
        while (await stream.ReadLineAsync(ct) is { } line)
        {
            output.AppendLine(line);

            const string marker = "Now listening on: ";
            if (listening is not null && line.IndexOf(marker, StringComparison.Ordinal) is var index && index >= 0
                && Uri.TryCreate(line[(index + marker.Length)..], UriKind.Absolute, out var endpoint))
                listening.TrySetResult(endpoint);
        }
    }

    private static async Task VerifyProductionRoutesAsync(Uri endpoint, CancellationToken ct)
    {
        using var client = new HttpClient { BaseAddress = endpoint };
        using var files = await client.GetAsync("/api/files/", ct);
        using var orderSettings = await client.GetAsync("/api/order-sync/settings", ct);
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (files.StatusCode, orderSettings.StatusCode));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var csrf = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.Contains(csrf.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("__Host-OpenERP.Csrf=", StringComparison.Ordinal)
            && cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
