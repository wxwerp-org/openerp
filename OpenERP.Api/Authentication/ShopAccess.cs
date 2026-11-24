using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Authentication;

internal sealed class ShopAccess
{
    private readonly Guid? _userId;
    internal bool IsAdministrator { get; }

    private ShopAccess(ClaimsPrincipal user)
    {
        IsAdministrator = user.IsInRole(UserAccount.AdministratorRole);
        if (Guid.TryParseExact(user.FindFirstValue(JwtRegisteredClaimNames.Sub), "D", out var id))
            _userId = id;
    }

    internal static ShopAccess For(ClaimsPrincipal user) => new(user);

    internal IQueryable<UserShopAssignment> Assignments(AppDbContext db) =>
        db.UserShopAssignments.Where(row => row.UserId == _userId);

    internal IQueryable<Shop> Shops(AppDbContext db)
    {
        var assigned = Assignments(db);
        return IsAdministrator ? db.Shops : db.Shops.Where(shop =>
            assigned.Any(row => row.Platform == shop.Platform && row.Sid == shop.Sid));
    }

    internal IQueryable<OrderRecord> Orders(AppDbContext db)
    {
        var assigned = Assignments(db);
        return IsAdministrator ? db.Orders : db.Orders.Where(order =>
            assigned.Any(row => row.Platform == order.Platform && row.Sid == order.Sid));
    }

    internal Task<bool> AllowsAsync(AppDbContext db, string? platform, int sid, CancellationToken ct) =>
        IsAdministrator ? Task.FromResult(true) : Assignments(db)
            .AnyAsync(row => row.Sid == sid && (platform == null || row.Platform == platform), ct);
}
