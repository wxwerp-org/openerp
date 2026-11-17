using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenERP.Api.Authentication;

internal sealed class AccountStore(
    AppDbContext db,
    DbContextOptions<AppDbContext> options,
    IPasswordHasher<AdminAccount> passwords,
    TimeProvider clock)
{
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    internal static bool IsValidPassword(string? password) => password is { Length: >= 6 and <= 20 };

    internal async Task SeedAsync(CancellationToken ct)
    {
        var admin = new AdminAccount();
        await using var write = CreateWriteContext();
        await write.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash")
            VALUES ({admin.Id}, {admin.Name}, NULL)
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
    }

    internal async Task<bool> IsInitializedAsync(CancellationToken ct) =>
        await db.AdminAccounts.Where(admin => admin.Id == AdminAccount.SingletonId)
            .Select(admin => (bool?)(admin.PasswordHash != null)).SingleOrDefaultAsync(ct)
        ?? throw new AccountStoreUnavailableException();

    internal async Task<bool> InitializeAsync(string newPassword, CancellationToken ct)
    {
        if (await IsInitializedAsync(ct))
            return false;

        var hash = passwords.HashPassword(new AdminAccount(), newPassword);
        await using var write = CreateWriteContext();
        // UPDATE 与登录和 CLI 重置争用同一行，等待后由 PostgreSQL 重新判定 NULL 条件。
        var updated = await write.AdminAccounts
            .Where(admin => admin.Id == AdminAccount.SingletonId && admin.PasswordHash == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(admin => admin.PasswordHash, hash), ct);
        if (updated == 1)
            return true;

        // 区分另一个请求已经初始化和运行中的数据库丢失管理员行。
        if (!await IsInitializedAsync(ct))
            throw new AccountStoreUnavailableException();

        return false;
    }

    internal async Task<AdminSession?> LoginAsync(string? username, string? password, CancellationToken ct)
    {
        if (username != AdminAccount.Username || !IsValidPassword(password))
            return null;

        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        if (admin is null)
            throw new AccountStoreUnavailableException();

        if (admin.PasswordHash is null)
            return null;

        var verification = passwords.VerifyHashedPassword(admin, admin.PasswordHash, password!);
        if (verification == PasswordVerificationResult.Failed)
            return null;

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            admin.PasswordHash = passwords.HashPassword(admin, password!);

        // PostgreSQL 时间戳保留微秒精度，首次响应与后续数据库读取保持一致。
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10));
        await write.AdminSessions.Where(session => session.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        admin.LastLoginAt = now;
        var session = new AdminSession { Id = Guid.NewGuid(), Admin = admin, ExpiresAt = now + SessionLifetime };
        write.AdminSessions.Add(session);
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    internal Task<AdminSession?> FindSessionAsync(Guid id, CancellationToken ct) =>
        db.AdminSessions.AsNoTracking().Include(session => session.Admin)
            .SingleOrDefaultAsync(session => session.Id == id && session.ExpiresAt > clock.GetUtcNow() && session.Admin.PasswordHash != null, ct);

    internal async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        await using var write = CreateWriteContext();
        await write.AdminSessions.Where(session => session.Id == id).ExecuteDeleteAsync(ct);
    }

    internal async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid sessionId, string? oldPassword, string newPassword, CancellationToken ct)
    {
        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        if (admin is null)
            throw new AccountStoreUnavailableException();

        // 认证中间件与持锁之间，改密或重置可能已撤销本次会话。
        if (admin.PasswordHash is null || !await write.AdminSessions.AnyAsync(
                session => session.Id == sessionId && session.ExpiresAt > clock.GetUtcNow(), ct))
            return PasswordChangeResult.SessionExpired;

        if (!IsValidPassword(oldPassword) ||
            passwords.VerifyHashedPassword(admin, admin.PasswordHash, oldPassword!) == PasswordVerificationResult.Failed)
            return PasswordChangeResult.InvalidPassword;

        await ReplacePasswordAsync(write, admin, newPassword, ct);
        await transaction.CommitAsync(ct);
        return PasswordChangeResult.Changed;
    }

    internal async Task<bool> ResetPasswordAsync(string newPassword, CancellationToken ct)
    {
        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        if (admin is null)
            return false;

        await ReplacePasswordAsync(write, admin, newPassword, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal async Task<AdminSession?> UpdateProfileAsync(
        Guid sessionId, string name, string? phone, string? email, CancellationToken ct)
    {
        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        if (admin is null)
            throw new AccountStoreUnavailableException();

        var session = await write.AdminSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId && session.ExpiresAt > clock.GetUtcNow(), ct);
        if (admin.PasswordHash is null || session is null)
            return null;

        admin.Name = name;
        admin.Phone = phone;
        admin.Email = email;
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    private AppDbContext CreateWriteContext()
    {
        // 连接在提交时中断可能已有写入结果，避免自动重放整笔认证事务。
        var writeOptions = new DbContextOptionsBuilder<AppDbContext>(options)
            .UseNpgsql(npgsql => npgsql.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)))
            .Options;
        return new AppDbContext(writeOptions);
    }

    private static async Task<AdminAccount?> LockAdminAsync(AppDbContext write, CancellationToken ct)
    {
        // 登录、改密与重置均先锁同一行，再检查密码或修改会话。
        var accounts = await write.AdminAccounts.FromSqlInterpolated(
            $"""SELECT * FROM "AdminAccounts" WHERE "Id" = {AdminAccount.SingletonId} FOR UPDATE""")
            .ToListAsync(ct);
        return accounts.SingleOrDefault();
    }

    private async Task ReplacePasswordAsync(AppDbContext write, AdminAccount admin, string newPassword, CancellationToken ct)
    {
        admin.PasswordHash = passwords.HashPassword(admin, newPassword);
        await write.SaveChangesAsync(ct);
        await write.AdminSessions.Where(session => session.AdminId == admin.Id).ExecuteDeleteAsync(ct);
    }
}

internal enum PasswordChangeResult
{
    Changed,
    InvalidPassword,
    SessionExpired
}

internal sealed class AccountStoreUnavailableException(Exception? innerException = null)
    : Exception("管理员认证存储不可用。", innerException);
