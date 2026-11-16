using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpResult;

namespace OpenERP.Api.Authentication;

internal sealed class AccountStore(
    AppDbContext db,
    DbContextOptions<AppDbContext> options,
    IPasswordHasher<AdminAccount> passwords,
    TimeProvider clock)
{
    internal const string InitialPasswordKey = "Authentication:InitialPassword";
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    internal static bool IsValidPassword(string? password) => password is { Length: >= 6 and <= 20 };

    internal async Task<OpResult.OpResult> SeedAsync(string? initialPassword, CancellationToken ct)
    {
        if (await db.AdminAccounts.AnyAsync(ct))
            return OpResults.Ok();

        if (!IsValidPassword(initialPassword))
            return OpResults.Err($"首次启动请配置 {InitialPasswordKey}（环境变量 Authentication__InitialPassword），密码长度须为 6–20 个字符。");

        var admin = new AdminAccount();
        var hash = passwords.HashPassword(admin, initialPassword!);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash", "MustChangePassword")
            VALUES ({admin.Id}, {admin.Name}, {hash}, TRUE)
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
        return OpResults.Ok();
    }

    internal async Task<AdminSession?> LoginAsync(string? username, string? password, CancellationToken ct)
    {
        if (username != AdminAccount.Username || !IsValidPassword(password))
            return null;

        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        if (admin is null)
            throw new InvalidOperationException("未找到管理员账号，请检查数据库初始化。");

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
            .SingleOrDefaultAsync(session => session.Id == id && session.ExpiresAt > clock.GetUtcNow(), ct);

    internal Task RevokeAsync(Guid id, CancellationToken ct) =>
        db.AdminSessions.Where(session => session.Id == id).ExecuteDeleteAsync(ct);

    internal async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid sessionId, string? oldPassword, string newPassword, CancellationToken ct)
    {
        await using var write = CreateWriteContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockAdminAsync(write, ct);
        // 认证中间件与持锁之间，改密或重置可能已撤销本次会话。
        if (admin is null || !await write.AdminSessions.AnyAsync(
                session => session.Id == sessionId && session.ExpiresAt > clock.GetUtcNow(), ct))
            return PasswordChangeResult.SessionExpired;

        // 首次设置由已验证的受限会话授权；普通改密仍须再次验证原密码。
        if (!admin.MustChangePassword && (!IsValidPassword(oldPassword) ||
            passwords.VerifyHashedPassword(admin, admin.PasswordHash, oldPassword!) == PasswordVerificationResult.Failed))
            return PasswordChangeResult.InvalidPassword;

        if (admin.MustChangePassword &&
            passwords.VerifyHashedPassword(admin, admin.PasswordHash, newPassword) != PasswordVerificationResult.Failed)
            return PasswordChangeResult.MustUseDifferentPassword;

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
        var session = await write.AdminSessions.SingleOrDefaultAsync(
            session => session.Id == sessionId && session.ExpiresAt > clock.GetUtcNow(), ct);
        if (admin is null || session is null)
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
        admin.MustChangePassword = false;
        await write.SaveChangesAsync(ct);
        await write.AdminSessions.Where(session => session.AdminId == admin.Id).ExecuteDeleteAsync(ct);
    }
}

internal enum PasswordChangeResult
{
    Changed,
    InvalidPassword,
    SessionExpired,
    MustUseDifferentPassword
}
