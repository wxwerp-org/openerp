using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Authentication;

internal sealed partial class AccountStore
{
    private static readonly TimeSpan UsernameReservation = TimeSpan.FromDays(7);
    private const string UsernameUnavailable = "该用户名正在使用或处于删除保留期。";

    internal Task<UserAccount[]> ListUsersAsync(CancellationToken ct) =>
        db.Users.AsNoTracking().OrderBy(user => user.Username).ToArrayAsync(ct);

    internal Task<UserAccount?> FindUserAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, ct);

    internal async Task<OpResult<UserAccount>> CreateUserAsync(
        string username, string password, ProfileChanges changes, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        await LockUserManagementAsync(write, ct);

        if (await IsUsernameTakenAsync(write, username, ct))
            return OpResults.Err(UsernameUnavailable);

        var user = new UserAccount { Username = username, CreatedAt = UtcNow() };
        changes.Apply(user);
        user.PasswordHash = passwords.HashPassword(user, password);
        write.Users.Add(user);
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return OpResults.Ok(user);
    }

    internal async Task<(OpResult<UserAccount> Result, int Status)> UpdateUserAsync(
        Guid id, string username, ProfileChanges changes, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        await LockUserManagementAsync(write, ct);
        var user = await LockByIdAsync(write, id, ct);
        if (user is null)
            return (OpResults.Err("用户不存在。"), StatusCodes.Status404NotFound);
        if (user.IsAdministrator)
            return (OpResults.Err("请在个人资料中修改管理员资料。"), StatusCodes.Status409Conflict);
        if (username != user.Username && await IsUsernameTakenAsync(write, username, ct))
            return (OpResults.Err(UsernameUnavailable), StatusCodes.Status409Conflict);

        user.Username = username;
        changes.Apply(user);
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (OpResults.Ok(user), StatusCodes.Status200OK);
    }

    internal async Task<(OpResult.OpResult Result, int Status)> DeleteUserAsync(Guid id, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        await LockUserManagementAsync(write, ct);
        var user = await LockByIdAsync(write, id, ct);
        if (user is null)
            return (OpResults.Err("用户不存在。"), StatusCodes.Status404NotFound);
        if (user.IsAdministrator)
            return (OpResults.Err("不能删除管理员。"), StatusCodes.Status409Conflict);

        var now = UtcNow();
        await RevokeUserSessionsAsync(write, id, now, ct);
        await write.UserShopAssignments.Where(row => row.UserId == id).ExecuteDeleteAsync(ct);
        user.DeletedAt = now;
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (OpResults.Ok(), StatusCodes.Status204NoContent);
    }

    private async Task<bool> IsUsernameTakenAsync(AppDbContext write, string username, CancellationToken ct)
    {
        var cutoff = UtcNow() - UsernameReservation;
        return await write.Users.IgnoreQueryFilters().AnyAsync(
            user => user.Username == username && (user.DeletedAt == null || user.DeletedAt > cutoff), ct);
    }

    private async Task LockUserManagementAsync(AppDbContext write, CancellationToken ct)
    {
        // 以跨进程的管理员行锁串行化用户名占用和释放，再锁修改目标。
        if (await LockByUsernameAsync(write, UserAccount.AdminUsername, ct) is null)
            throw new AccountStoreUnavailableException();
    }
}
