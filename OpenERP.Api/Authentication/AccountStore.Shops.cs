using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Authentication;

/// <summary>具有稳定身份的店铺，即使本地目录暂时移除它也能识别原分配。</summary>
internal sealed record AssignedShop(string Platform, int Sid);

internal sealed partial class AccountStore
{
    internal Task<AssignedShop[]> GetShopAssignmentsAsync(Guid id, CancellationToken ct) =>
        db.UserShopAssignments.Where(row => row.UserId == id)
            .OrderBy(row => row.Platform).ThenBy(row => row.Sid)
            .Select(row => new AssignedShop(row.Platform, row.Sid)).ToArrayAsync(ct);

    internal async Task<(OpResult.OpResult Result, int Status)> SetShopAssignmentsAsync(
        Guid id, HashSet<AssignedShop> requested, CancellationToken ct)
    {
        await using var write = writes.CreateDbContext();
        await using var transaction = await write.Database.BeginTransactionAsync(ct);
        var user = await LockByIdAsync(write, id, ct);
        if (user is null)
            return (OpResults.Err("用户不存在。"), StatusCodes.Status404NotFound);
        if (user.IsAdministrator)
            return (OpResults.Err("管理员已拥有全部店铺权限。"), StatusCodes.Status409Conflict);

        var current = await write.UserShopAssignments.Where(row => row.UserId == id).ToListAsync(ct);
        var additions = requested.Except(current.Select(row => new AssignedShop(row.Platform, row.Sid))).ToArray();
        if (additions.Length > 0)
        {
            var sids = additions.Select(shop => shop.Sid).Distinct().ToArray();
            var available = await write.Shops.Where(shop => sids.Contains(shop.Sid))
                .Select(shop => new AssignedShop(shop.Platform, shop.Sid)).ToListAsync(ct);
            if (additions.Except(available).Any())
                return (OpResults.Err("所选店铺已不在本地目录，请刷新后重试。"), StatusCodes.Status400BadRequest);

            write.UserShopAssignments.AddRange(additions.Select(shop =>
                new UserShopAssignment { UserId = id, Platform = shop.Platform, Sid = shop.Sid }));
        }
        write.UserShopAssignments.RemoveRange(current.Where(row => !requested.Contains(new(row.Platform, row.Sid))));
        await write.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (OpResults.Ok(), StatusCodes.Status204NoContent);
    }
}
