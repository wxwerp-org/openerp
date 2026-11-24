using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenERP.Api.Authentication;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class SqliteAuthenticationStoreTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("openerp-sqlite-auth-");

    [Fact]
    public async Task Concurrent_seed_creates_one_uninitialized_administrator()
    {
        // Given 两个独立连接都在空库中准备创建管理员。
        var ct = TestContext.Current.CancellationToken;
        var options = CreateOptions(new ConcurrentSaves());
        await using var first = new AppDbContext(options);
        await first.Database.EnsureCreatedAsync(ct);
        await using var second = new AppDbContext(options);

        // When 同时尝试插入同一用户名；Then 两个启动都成功，只留下一个未初始化管理员。
        await Task.WhenAll(CreateStore(first, options).SeedAsync(ct), CreateStore(second, options).SeedAsync(ct));
        var admin = Assert.Single(await first.Users.AsNoTracking().ToListAsync(ct));
        Assert.Equal(UserAccount.AdminUsername, admin.Username);
        Assert.Equal("管理员", admin.Name);
        Assert.Null(admin.PasswordHash);
    }

    [Fact]
    public async Task Password_reset_revokes_old_session_and_prevents_competing_old_password_login()
    {
        // Given 已初始化 SQLite 管理员与一个可用会话。
        var ct = TestContext.Current.CancellationToken;
        const string oldPassword = "sqlite-old-password";
        const string newPassword = " sqlite-new-pass ";
        var options = CreateOptions();
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = CreateStore(db, options);
        await store.SeedAsync(ct);
        Assert.True(await store.InitializeAsync(oldPassword, ct));
        var oldHash = RandomNumberGenerator.GetBytes(32);
        var oldToken = await store.LoginAsync(UserAccount.AdminUsername, oldPassword, oldHash, ct);
        Assert.NotNull(oldToken);
        Assert.Equal(oldToken.Id, (await store.FindActiveRefreshTokenAsync(oldHash, ct))?.Id);

        // When 重置持有事务，并且使用旧密码的登录从另一连接开始事务。
        var resetGate = new PausedSave();
        var resetOptions = CreateOptions(resetGate);
        await using var resetDb = new AppDbContext(resetOptions);
        var loginGate = new TransactionStartSignal();
        var loginOptions = CreateOptions(loginGate);
        await using var loginDb = new AppDbContext(loginOptions);
        var reset = CreateStore(resetDb, resetOptions).ResetPasswordAsync(newPassword, ct);
        Task<UserRefreshToken?>? login = null;
        try
        {
            await resetGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            // Microsoft.Data.Sqlite 同步等待数据库锁，故在另一线程发起登录。
            login = Task.Run(() => CreateStore(loginDb, loginOptions)
                .LoginAsync(UserAccount.AdminUsername, oldPassword, RandomNumberGenerator.GetBytes(32), ct), ct);
            await loginGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            resetGate.Release.TrySetResult();
            await Task.WhenAll(reset, login ?? Task.CompletedTask);
        }

        // Then 重置成功，两种旧凭据均无效，新密码能签发会话。
        Assert.True(await reset);
        Assert.Null(await login!);
        Assert.Null(await store.FindActiveRefreshTokenAsync(oldToken.Id, ct));
        Assert.Null(await store.FindActiveRefreshTokenAsync(oldHash, ct));
        Assert.NotNull(await store.LoginAsync(UserAccount.AdminUsername, newPassword, RandomNumberGenerator.GetBytes(32), ct));
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private DbContextOptions<AppDbContext> CreateOptions(params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory.FullName, "openerp.db"),
            Pooling = false
        };
        return new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ToString())
            .AddInterceptors(interceptors).Options;
    }

    private static AccountStore CreateStore(AppDbContext db, DbContextOptions<AppDbContext> options) =>
        new(db, options, new PasswordHasher<UserAccount>(), TimeProvider.System);

    private sealed class ConcurrentSaves : SaveChangesInterceptor
    {
        private int _arrived;
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == 2)
                _bothArrived.SetResult();
            await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class PausedSave : SaveChangesInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class TransactionStartSignal : DbTransactionInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
