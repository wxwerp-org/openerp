using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
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

internal sealed record OrderListRequest(int? ShopSid, string? Platform, string? Status, string? Number,
    DateOnly? CreatedFrom, DateOnly? CreatedTo, int Page, int PageSize);

/// <summary>筛选后的本地订单分页及可见本地订单的筛选选项。</summary>
public sealed record OrderListResponse(IReadOnlyList<OrderListItem> Items, long Total, int Page, int PageSize,
    OrderListOptions Options);

/// <summary>本地 ERP 主单及其关联平台单，时间按上游来源表达。</summary>
public sealed record OrderListItem(string Id, int Sid, string? ShopName, string Platform,
    IReadOnlyList<PlatformOrderListItem> PlatformOrders, string? Created, string? Dispatched, int LineCount, long ItemQuantity);

/// <summary>平台订单号、状态和当前号码及状态条件的命中标记。</summary>
public sealed record PlatformOrderListItem(string Number, string Status, bool Matched);

/// <summary>可见本地订单中存在的店铺、平台与平台子单状态。</summary>
public sealed record OrderListOptions(IReadOnlyList<OrderListShop> Shops, IReadOnlyList<string> Platforms,
    IReadOnlyList<string> Statuses);

/// <summary>本地订单的店铺 SID 及可用的名称。</summary>
public sealed record OrderListShop(int Sid, string? Name);
