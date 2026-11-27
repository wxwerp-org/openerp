using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;

using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

internal static class OrderReportEndpoints
{
    internal static void MapOrderReport(this WebApplication app)
    {
        app.MapGet("/api/orders/report", async Task<Results<Ok<OrderReportResponse>, ProblemHttpResult>>
            (OrderReportQuery query, HttpContext context, int? shopSid, string? platform, string? createdFrom,
                string? createdTo, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (shopSid is <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须大于0。");
            if (!TryParseDate(createdFrom, out var from) || !TryParseDate(createdTo, out var to) || from > to)
                return TypedResults.Problem(statusCode: 400, title: "创建日期必须使用 yyyy-MM-dd 格式，且起始日期不能晚于结束日期。");

            var report = await query.ReadAsync(new OrderReportRequest(shopSid, platform, from, to), ShopAccess.For(context.User), ct);
            return report is null
                ? TypedResults.Problem(statusCode: 403, title: "无权查看该店铺。")
                : TypedResults.Ok(report);
        }).WithSummary("按日期、店铺、平台、状态、物流和商品统计本地订单")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static bool TryParseDate(string? input, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrEmpty(input))
            return true;
        if (!DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return false;

        date = value;
        return true;
    }
}

internal sealed record OrderReportRequest(int? ShopSid, string? Platform, DateOnly? CreatedFrom, DateOnly? CreatedTo);

/// <summary>订单报表：汇总、按日趋势，以及店铺、平台、订单状态、物流渠道四个维度和热销商品。</summary>
public sealed record OrderReportResponse(OrderReportSummary Summary, IReadOnlyList<OrderReportBucket> Daily,
    IReadOnlyList<OrderReportBucket> Shops, IReadOnlyList<OrderReportBucket> Platforms,
    IReadOnlyList<OrderReportBucket> Statuses, IReadOnlyList<OrderReportBucket> Logistics,
    IReadOnlyList<OrderReportProduct> Products, OrderReportOptions Options);

/// <summary>总览：订单数、商品件数、已付款与已出库订单数，金额按币种分列（不同币种不换算）。</summary>
public sealed record OrderReportSummary(int Orders, long ItemQuantity, int Dispatched, int Paid,
    IReadOnlyList<OrderReportAmount> Amounts);

/// <summary>某个维度取值下的订单数、件数和按币种的金额。</summary>
public sealed record OrderReportBucket(string Key, string Label, int Orders, long ItemQuantity,
    IReadOnlyList<OrderReportAmount> Amounts);

/// <summary>某币种下有金额的订单数及金额合计。</summary>
public sealed record OrderReportAmount(string Currency, int Orders, decimal Total);

/// <summary>按商品（商品 ID 与 SKU）统计的销量。</summary>
public sealed record OrderReportProduct(string? ProductId, string? SkuCode, string? Name, string? ImageUrl, long Quantity,
    int Orders);

/// <summary>可见订单中的店铺与平台，用于报表筛选。</summary>
public sealed record OrderReportOptions(IReadOnlyList<OrderListShop> Shops, IReadOnlyList<string> Platforms);
