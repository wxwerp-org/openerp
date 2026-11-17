using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed partial class AuthenticationStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cli_output_is_captured_by_xunit_and_redacted_for_both_exit_codes(bool accountExists)
    {
        // Given 已迁移的独立数据库；有账号时 CLI 成功，没有账号时 CLI 失败。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        if (accountExists)
            await CreateStore(db, TimeProvider.System).SeedAsync(ct);
        const string password = "output-secret-9b4c";

        // When 真实 CLI 包装器向两个流写出标记，并模拟子进程意外输出密码。
        var result = await RunApiOnDatabaseAsync(_connectionString, password,
            ["reset-admin-password", "--stdin"], ct, configureStart: start =>
            {
                StartWithOutputMarkers(start);
                start.Environment["OPENERP_TEST_OUTPUT_SECRET"] = password;
                if (OperatingSystem.IsWindows())
                    start.Arguments = start.Arguments.Replace("echo cli-stdout-marker",
                        "echo %OPENERP_TEST_OUTPUT_SECRET% & echo cli-stdout-marker", StringComparison.Ordinal);
                else
                    start.ArgumentList[1] = "printf '%s\\n' \"$OPENERP_TEST_OUTPUT_SECRET\"; " + start.ArgumentList[1];
            });

        // Then 普通退出码断言可用；xUnit 收到两个流且脱敏，原始输出仍可用于泄漏检查。
        Assert.Equal(accountExists ? 0 : 1, result.ExitCode);
        Assert.True(result.Output.Contains(password, StringComparison.Ordinal), "未观察到注入的原始输出。");
        var output = Assert.IsAssignableFrom<ITestOutputHelper>(TestContext.Current.TestOutputHelper).Output;
        Assert.Contains("cli-stdout-marker", output);
        Assert.Contains("cli-stderr-marker", output);
        Assert.Contains("[已隐藏]", output);
        Assert.False(output.Contains(password, StringComparison.Ordinal), "测试输出泄露了密码。");
        Assert.False(output.Contains(_connectionString, StringComparison.Ordinal), "测试输出泄露了连接字符串。");
    }

    [Fact]
    public async Task Invalid_cli_arguments_report_the_real_process_exit_before_the_lock_deadline()
    {
        // Given 等待真实 CLI 的管理员锁，但 argv 无效，CLI 会输出用法并提前退出。
        var ct = TestContext.Current.CancellationToken;
        await using var observer = new NpgsqlConnection(_connectionString);
        await observer.OpenAsync(ct);
        var reset = RunApiAsync(null, ct, "reset-admin-password", "unused-argument");

        // When 观察锁；Then 立即得到阶段、锁数量和真实退出输出。
        var failure = await Record.ExceptionAsync(() =>
            AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "等待无效 CLI 参数的锁",
                [new("重置密码 CLI", reset, () => $"退出码 {reset.Result.ExitCode}")]));
        var result = await reset;
        Assert.Equal(1, result.ExitCode);
        Assert.True(result.Output.Contains("从重定向的标准输入读取一行密码", StringComparison.Ordinal));
        var diagnostic = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("从重定向的标准输入读取一行密码", TestContext.Current.TestOutputHelper!.Output);
        Assert.Contains("等待无效 CLI 参数的锁", diagnostic.Message);
        Assert.Contains("提前结束（退出码 1", diagnostic.Message);
        Assert.Contains("期待 1 个管理员锁等待，观察到 0 个", diagnostic.Message);
        Assert.Contains("相关数据库活动", diagnostic.Message);
    }

    [Fact]
    public async Task Cli_launch_delayed_beyond_five_seconds_can_still_wait_on_the_real_admin_lock()
    {
        // Given 独立数据库已迁移，测试事务持有管理员行锁。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        await CreateStore(db, TimeProvider.System).SeedAsync(ct);
        await using var observer = new NpgsqlConnection(_connectionString);
        await observer.OpenAsync(ct);
        await using var transaction = await observer.BeginTransactionAsync(ct);
        await using var lockAdmin = new NpgsqlCommand(
            """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
        Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

        // When 测试包装器延迟真实 CLI 启动；Then 能观察真实锁且 CLI 在解锁后成功。
        using var operations = new CancellationTokenSource();
        var reset = RunDelayedResetAsync(operations.Token);
        Exception? failure = null;
        try
        {
            await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "等待延迟启动的 CLI",
                [new("重置密码 CLI", reset)]);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            await AuthenticationConcurrency.ReleaseAsync(transaction.RollbackAsync, operations, failure, ct, reset);
        }

        var result = await reset;
        Assert.Equal(0, result.ExitCode);

        async Task<(int ExitCode, string Output, bool Started)> RunDelayedResetAsync(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(6), token);
            return await RunApiAsync(fixture.AdminPassword, token, "reset-admin-password", "--stdin");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blocked_cli_cancel_or_timeout_preserves_output_and_allows_database_drop(bool cancel)
    {
        // Given 真实 CLI 启动前写出标准输出和标准错误，并被数据库事务阻塞。
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync(ct);
            await CreateStore(db, TimeProvider.System).SeedAsync(ct);
        }

        using var cancellation = new CancellationTokenSource();
        Exception? failure;
        await using (var observer = new NpgsqlConnection(_connectionString))
        {
            await observer.OpenAsync(ct);
            await using var transaction = await observer.BeginTransactionAsync(ct);
            await using var lockAdmin = new NpgsqlCommand(
                """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
            Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

            var reset = RunApiOnDatabaseAsync(_connectionString, fixture.AdminPassword,
                ["reset-admin-password", "--stdin"], cancellation.Token,
                configureStart: StartWithOutputMarkers, processBudget: TimeSpan.FromSeconds(10));
            Exception? coordinationFailure = null;
            try
            {
                await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "等待 CLI 被管理员行锁阻塞",
                    [new("重置密码 CLI", reset)]);
                // When 取消或等待超时；Then 退出后还保留两个输出流。
                if (cancel)
                    await cancellation.CancelAsync();
                failure = await Record.ExceptionAsync(async () => await reset);
            }
            catch (Exception error)
            {
                coordinationFailure = error;
                throw;
            }
            finally
            {
                // 进程失败已由本用例显式观察；异常路径仍须释放事务并回收进程。
                await AuthenticationConcurrency.ReleaseAsync(transaction.RollbackAsync, cancellation, coordinationFailure, ct,
                    coordinationFailure is null ? null : reset);
            }
        }

        // Then 进程已经结束，独立数据库可直接删除。
        await DisposeAsync();
        var diagnostic = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("cli-stdout-marker", diagnostic.Message);
        Assert.Contains("cli-stderr-marker", diagnostic.Message);
        Assert.Contains(cancel ? "操作已取消" : "操作超时", diagnostic.Message);
        Assert.DoesNotContain(fixture.AdminPassword, diagnostic.Message);
        Assert.DoesNotContain(_connectionString, diagnostic.Message);
    }

    [Fact]
    public async Task Lock_wait_deadline_preserves_the_diagnostic_and_reaps_the_blocked_cli()
    {
        // Given 独立库中的一条真实管理员锁等待。
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync(ct);
            await CreateStore(db, TimeProvider.System).SeedAsync(ct);
        }

        Exception? primaryError;
        Exception? failure;
        Task<(int ExitCode, string Output, bool Started)> reset;
        await using (var observer = new NpgsqlConnection(_connectionString))
        {
            await observer.OpenAsync(ct);
            await using var transaction = await observer.BeginTransactionAsync(ct);
            await using var lockAdmin = new NpgsqlCommand(
                """SELECT "Id" FROM "AdminAccounts" WHERE "Id" = 1 FOR UPDATE""", observer, transaction);
            Assert.Equal(1, await lockAdmin.ExecuteScalarAsync(ct));

            using var operations = new CancellationTokenSource();
            reset = RunApiAsync(fixture.AdminPassword, operations.Token, "reset-admin-password", "--stdin");
            var resetOperation = new BlockedOperation("重置密码 CLI", reset);
            // When 等待不存在的第二条锁；Then 释放持锁事务后取消并观察已启动的 CLI。
            primaryError = await Record.ExceptionAsync(async () =>
            {
                await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 1, ct, "确认 CLI 被阻塞",
                    [resetOperation]);
                await AuthenticationConcurrency.WaitForAdminLocksAsync(observer, 2, ct, "等待第二条管理员锁",
                    [resetOperation], budget: TimeSpan.FromMilliseconds(500));
            });
            failure = await Record.ExceptionAsync(() =>
                AuthenticationConcurrency.ReleaseAsync(transaction.RollbackAsync, operations, primaryError, ct, reset));
            Assert.True(operations.IsCancellationRequested);
        }

        Assert.True(reset.IsCompleted);
        await DisposeAsync();
        var diagnostic = Assert.IsType<InvalidOperationException>(primaryError);
        Assert.Contains("等待第二条管理员锁：等待锁超时", diagnostic.Message);
        Assert.Contains("期待 2 个管理员锁等待，观察到 1 个", diagnostic.Message);
        Assert.Contains("wait=Lock/", diagnostic.Message);
        Assert.Same(diagnostic, failure is AggregateException aggregate ? aggregate.InnerExceptions[0] : failure);
    }

    private static void StartWithOutputMarkers(ProcessStartInfo start)
    {
        var executable = start.FileName;
        if (OperatingSystem.IsWindows())
        {
            var assembly = start.ArgumentList[0];
            start.ArgumentList.Clear();
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            start.Arguments = $"""
                /d /s /c "echo cli-stdout-marker & echo cli-stderr-marker 1>&2 & "{executable}" "{assembly}" reset-admin-password --stdin"
                """;
            return;
        }

        start.FileName = "/bin/sh";
        start.ArgumentList.Insert(0, executable);
        start.ArgumentList.Insert(0, "openerp-cli-wrapper");
        start.ArgumentList.Insert(0, "printf 'cli-stdout-marker\\n'; printf 'cli-stderr-marker\\n' >&2; exec \"$@\"");
        start.ArgumentList.Insert(0, "-c");
    }
}
