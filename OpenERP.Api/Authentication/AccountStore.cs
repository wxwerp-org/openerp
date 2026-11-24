using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OpenERP.Api.Authentication;

internal static class AccountStoreRegistration
{
    internal static void AddAccountStore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AccountStore>();
        services.AddScoped<IAdministratorPasswordReset>(services => services.GetRequiredService<AccountStore>());
        services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
    }
}

/// <summary>认证写入使用独立上下文，并在当前写事务中读取待修改的账号。</summary>
internal interface IAccountWrites : IDbContextFactory<AppDbContext>
{
    Task EnsureExistsAsync(UserAccount account, CancellationToken cancellationToken);

    IQueryable<UserAccount> UsersForUpdate(AppDbContext write);
}

internal sealed partial class AccountStore(
    AppDbContext db,
    IAccountWrites writes,
    IPasswordHasher<UserAccount> passwords,
    TimeProvider clock) : IAdministratorPasswordReset
{
    internal static readonly TimeSpan RefreshLifetime = TimeSpan.FromHours(24);

    internal static bool IsValidPassword(string? password) => password is { Length: >= 6 and <= 20 };

    internal Task SeedAsync(CancellationToken ct) =>
        writes.EnsureExistsAsync(new UserAccount { Username = UserAccount.AdminUsername, Name = "管理员", CreatedAt = UtcNow() }, ct);

    internal async Task<bool> IsInitializedAsync(CancellationToken ct) =>
        await db.Users.Where(user => user.Username == UserAccount.AdminUsername)
            .Select(user => (bool?)(user.PasswordHash != null)).SingleOrDefaultAsync(ct)
        ?? throw new AccountStoreUnavailableException();

    internal async Task<bool> InitializeAsync(string newPassword, CancellationToken ct)
    {
        if (await IsInitializedAsync(ct))
            return false;

        var hash = passwords.HashPassword(new UserAccount { Username = UserAccount.AdminUsername }, newPassword);
        await using var write = writes.CreateDbContext();
        // UPDATE 与登录和 CLI 重置争用同一行，写事务同时校验 NULL 条件。
        var updated = await write.Users
            .Where(user => user.Username == UserAccount.AdminUsername && user.PasswordHash == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PasswordHash, hash), ct);
        if (updated == 1)
            return true;

        // 区分另一个请求已经初始化和运行中的数据库丢失管理员行。
        if (!await IsInitializedAsync(ct))
            throw new AccountStoreUnavailableException();

        return false;
    }

    internal async Task<UserRefreshToken?> LoginAsync(string? username, string? password, byte[] tokenHash, CancellationToken ct)
    {
        if (username is not { Length: >= 1 and <= 20 } || !IsValidPassword(password))
            return null;

        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var user = await LockByUsernameAsync(write, username, ct);
        if (user is null && username == UserAccount.AdminUsername)
            throw new AccountStoreUnavailableException();

        if (user?.PasswordHash is null)
            return null;

        var verification = passwords.VerifyHashedPassword(user, user.PasswordHash, password!);
        if (verification == PasswordVerificationResult.Failed)
            return null;

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwords.HashPassword(user, password!);

        // 会话时间统一到微秒，首次响应与后续数据库读取保持一致。
        var now = UtcNow();
        await MarkRevokedAsync(write.RefreshTokens.Where(token => token.ExpiresAt <= now), now, ct);
        user.LastLoginAt = now;
        var token = new UserRefreshToken
        {
            User = user, UserId = user.Id, TokenHash = tokenHash, ExpiresAt = now + RefreshLifetime
        };
        write.RefreshTokens.Add(token);
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return token;
    }

    internal Task<UserRefreshToken?> FindActiveRefreshTokenAsync(Guid id, CancellationToken ct) =>
        ActiveRefreshTokens().SingleOrDefaultAsync(token => token.Id == id, ct);

    internal Task<UserRefreshToken?> FindActiveRefreshTokenAsync(byte[] tokenHash, CancellationToken ct) =>
        ActiveRefreshTokens().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, ct);

    internal async Task RevokeAsync(byte[] tokenHash, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await MarkRevokedAsync(write.RefreshTokens.Where(token => token.TokenHash == tokenHash), UtcNow(), ct);
    }

    internal async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId, Guid loginId, string? oldPassword, string newPassword, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var user = await LockByIdAsync(write, userId, ct);

        // 认证中间件与持锁之间，改密、删除或重置可能已撤销本次会话。
        if (user?.PasswordHash is null || !await SessionQuery(write, userId, loginId).AnyAsync(ct))
            return PasswordChangeResult.SessionExpired;

        if (!IsValidPassword(oldPassword) ||
            passwords.VerifyHashedPassword(user, user.PasswordHash, oldPassword!) == PasswordVerificationResult.Failed)
            return PasswordChangeResult.InvalidPassword;

        await ReplacePasswordAsync(write, user, newPassword, ct);
        await transaction.CommitAsync(ct);
        return PasswordChangeResult.Changed;
    }

    public async Task<bool> ResetPasswordAsync(string newPassword, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var admin = await LockByUsernameAsync(write, UserAccount.AdminUsername, ct);
        if (admin is null)
            return false;

        await ReplacePasswordAsync(write, admin, newPassword, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal async Task<UserRefreshToken?> UpdateProfileAsync(
        Guid userId, Guid loginId, ProfileChanges changes, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var user = await LockByIdAsync(write, userId, ct);
        if (user?.PasswordHash is null)
            return null;

        var token = await SessionQuery(write, userId, loginId).SingleOrDefaultAsync(ct);
        if (token is null)
            return null;

        changes.Apply(user);
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return token;
    }

    private IQueryable<UserRefreshToken> SessionQuery(AppDbContext write, Guid userId, Guid loginId)
    {
        var now = clock.GetUtcNow();
        return write.RefreshTokens.Where(token => token.UserId == userId && token.Id == loginId && token.ExpiresAt > now);
    }

    private IQueryable<UserRefreshToken> ActiveRefreshTokens()
    {
        var now = clock.GetUtcNow();
        return db.RefreshTokens.AsNoTracking().Include(token => token.User)
            .Where(token => token.ExpiresAt > now && token.User.PasswordHash != null);
    }

    private Task<UserAccount?> LockByUsernameAsync(AppDbContext write, string username, CancellationToken ct) =>
        writes.UsersForUpdate(write).SingleOrDefaultAsync(user => user.Username == username, ct);

    private Task<UserAccount?> LockByIdAsync(AppDbContext write, Guid id, CancellationToken ct) =>
        writes.UsersForUpdate(write).SingleOrDefaultAsync(user => user.Id == id, ct);

    private async Task ReplacePasswordAsync(AppDbContext write, UserAccount user, string newPassword, CancellationToken ct)
    {
        user.PasswordHash = passwords.HashPassword(user, newPassword);
        await write.SaveChangesAsync(ct);
        await RevokeUserSessionsAsync(write, user.Id, UtcNow(), ct);
    }

    private static Task RevokeUserSessionsAsync(AppDbContext write, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        MarkRevokedAsync(write.RefreshTokens.Where(token => token.UserId == userId), now, ct);

    private static Task MarkRevokedAsync(IQueryable<UserRefreshToken> tokens, DateTimeOffset now, CancellationToken ct) =>
        tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.DeletedAt, now), ct);

    private DateTimeOffset UtcNow()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
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
