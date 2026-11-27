using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

internal static class OrderListEndpoints
{
    internal static void MapOrderList(this WebApplication app)
    {
        app.MapGet("/api/orders", async Task<Results<Ok<OrderListResponse>, ProblemHttpResult>>
            (OrderListQuery query, HttpContext context, int? shopSid, string? platform, string? status, string? number,
                string? createdFrom, string? createdTo, int? page, int? pageSize, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var selectedPage = page ?? 1;
            var selectedSize = pageSize ?? 20;
            if (selectedPage < 1 || selectedSize is < 1 or > 100 || (long)(selectedPage - 1) * selectedSize > int.MaxValue)
                return TypedResults.Problem(statusCode: 400, title: "页码必须大于0，每页1至100条，分页范围不能超出限制。");
            if (shopSid is <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须大于0。");
            if (!TryParseDate(createdFrom, out var from) || !TryParseDate(createdTo, out var to) || from > to)
                return TypedResults.Problem(statusCode: 400, title: "创建日期必须使用 yyyy-MM-dd 格式，且起始日期不能晚于结束日期。");

            var request = new OrderListRequest(shopSid, platform, status, number, from, to, selectedPage, selectedSize);
            var (result, responseStatus) = await query.ReadAsync(request, ShopAccess.For(context.User), ct);
            return result.IsErr
                ? TypedResults.Problem(statusCode: responseStatus, title: result.Error.Message)
                : TypedResults.Ok(result.Value);
        }).WithSummary("查询本地订单与关联的平台单号及状态")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    internal static void MapOrderDetail(this WebApplication app)
    {
        // 订单号含冒号（如 AliExpress:30:123），用查询参数传递，避免路径转义问题。
        app.MapGet("/api/orders/detail", async Task<Results<Ok<OrderListItem>, ProblemHttpResult>>
            (OrderListQuery query, HttpContext context, string id, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var order = await query.ReadOneAsync(id, ShopAccess.For(context.User), ct);
            return order is null
                ? TypedResults.Problem(statusCode: 404, title: "订单不存在，或无权查看。")
                : TypedResults.Ok(order);
        }).RequireAuthorization().WithSummary("查询单个本地订单的完整信息");
    }

    internal static void MapOrderDelete(this WebApplication app)
    {
        app.MapPost("/api/orders/delete", async Task<Results<Ok<OrderDeleteResponse>, ProblemHttpResult>>
            (OrderDeleteRequest request, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ids = request.Ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
            if (ids.Length is 0 or > 100)
                return TypedResults.Problem(statusCode: 400, title: "一次需选择1至100个订单。");

            // 只能删除自己有权限的店铺下的订单；无权或不存在的订单不会被计入。
            var access = ShopAccess.For(context.User);
            var now = DateTimeOffset.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var allowed = await access.Orders(db).Where(order => ids.Contains(order.ErpId)).Select(order => order.ErpId)
                .ToArrayAsync(ct);
            var deleted = await db.Orders.Where(order => allowed.Contains(order.ErpId))
                .ExecuteUpdateAsync(set => set.SetProperty(order => order.DeletedAt, now), ct);
            await db.Set<PlatformOrderRecord>().Where(po => allowed.Contains(po.ErpId))
                .ExecuteUpdateAsync(set => set.SetProperty(po => po.DeletedAt, now), ct);
            await db.Set<OrderLineRecord>().Where(line => allowed.Contains(line.ErpId))
                .ExecuteUpdateAsync(set => set.SetProperty(line => line.DeletedAt, now), ct);
            await transaction.CommitAsync(ct);
            return TypedResults.Ok(new OrderDeleteResponse(deleted));
        }).RequireAuthorization().WithSummary("删除本地订单（软删除，含平台子单与商品行）");
    }

    private static bool TryParseDate(string? input, out DateOnly? date)
    {
        date = null;
        if (input is null)
            return true;
        if (!DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return false;

        date = value;
        return true;
    }
}

/// <summary>要删除的本地订单（ERP 主单号，即列表里的订单 Id）。</summary>
public sealed record OrderDeleteRequest(IReadOnlyList<string>? Ids);

/// <summary>实际删除的订单数。</summary>
public sealed record OrderDeleteResponse(int Deleted);

internal sealed record OrderListRequest(int? ShopSid, string? Platform, string? Status, string? Number,
    DateOnly? CreatedFrom, DateOnly? CreatedTo, int Page, int PageSize);

/// <summary>筛选后的本地订单分页及可见本地订单的筛选选项。</summary>
public sealed record OrderListResponse(IReadOnlyList<OrderListItem> Items, long Total, int Page, int PageSize,
    OrderListOptions Options);

/// <summary>本地 ERP 主单及其关联平台单，时间按上游来源表达。</summary>
public sealed record OrderListItem(string Id, int Sid, string? ShopName, string Platform,
    IReadOnlyList<PlatformOrderListItem> PlatformOrders, string? Created, string? Dispatched, int LineCount, long ItemQuantity,
    IReadOnlyList<OrderLineListItem> Lines, string? Status, string? Buyer, decimal? TotalAmount, string? TotalCurrency,
    string? Logistics, string? Paid, IReadOnlyList<OrderDetail> Details);

/// <summary>订单中的一个商品行：名称、图片、规格（SKU 属性）、数量、单价、物流渠道与平台无关的补充信息。</summary>
public sealed record OrderLineListItem(string? ProductId, string? SkuCode, string? Name, string? ImageUrl, string? Specs,
    int Quantity, decimal? UnitPrice, string? Currency, string? Logistics, IReadOnlyList<OrderDetail> Details);

/// <summary>平台订单号、状态和当前号码及状态条件的命中标记。</summary>
public sealed record PlatformOrderListItem(string Number, string Status, bool Matched);

/// <summary>可见本地订单中存在的店铺、平台与平台子单状态。</summary>
public sealed record OrderListOptions(IReadOnlyList<OrderListShop> Shops, IReadOnlyList<string> Platforms,
    IReadOnlyList<string> Statuses);

/// <summary>本地订单的店铺 SID 及可用的名称。</summary>
public sealed record OrderListShop(int Sid, string? Name);
