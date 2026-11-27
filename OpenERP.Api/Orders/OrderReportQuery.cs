using System.Globalization;

using Microsoft.EntityFrameworkCore;

using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

/// <summary>订单报表：在可见本地订单上按创建日期、店铺、平台等条件统计订单数、件数与按币种分列的金额。</summary>
internal sealed class OrderReportQuery(AppDbContext db, OrderListQuery list)
{
    private const int TopProducts = 10;
    private const string NoLogistics = "未填写";
    private const string NoCurrency = "未知";

    internal async Task<OrderReportResponse?> ReadAsync(OrderReportRequest request, ShopAccess access, CancellationToken ct)
    {
        var platform = string.IsNullOrEmpty(request.Platform) ? null : request.Platform;
        if (request.ShopSid is { } shopSid && !await access.AllowsAsync(db, platform, shopSid, ct))
            return null;

        var orders = access.Orders(db).AsNoTracking();
        var lines = db.Set<OrderLineRecord>().AsNoTracking();
        var matched = from header in list.ReadHeaders()
                      join order in orders on header.ErpId equals order.ErpId
                      where (request.ShopSid == null || order.Sid == request.ShopSid)
                          && (platform == null || order.Platform == platform)
                          && (request.CreatedFrom == null || header.CreatedDate >= request.CreatedFrom)
                          && (request.CreatedTo == null || header.CreatedDate <= request.CreatedTo)
                      select new { header, order };
        var data = await matched.Select(m => new OrderReportRow(m.order.ErpId, m.order.Sid, m.order.Platform, m.order.Status,
            m.order.Logistics, m.order.TotalAmount, m.order.TotalCurrency, m.header.CreatedDate, m.header.DispatchedAt != null,
            m.order.PaidRaw != null && m.order.PaidRaw.Trim() != "",
            lines.Where(line => line.ErpId == m.order.ErpId).Sum(line => (long)line.Quantity))).ToArrayAsync(ct);

        var ids = matched.Select(m => m.order.ErpId);
        var products = await (from line in lines join id in ids on line.ErpId equals id select line)
            .GroupBy(line => new { line.ProductId, line.SkuCode })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.SkuCode,
                Name = group.Max(line => line.Name),
                ImageUrl = group.Max(line => line.ImageUrl),
                Quantity = group.Sum(line => (long)line.Quantity),
                Orders = group.Select(line => line.ErpId).Distinct().Count()
            })
            .OrderByDescending(product => product.Quantity).ThenBy(product => product.ProductId).ThenBy(product => product.SkuCode)
            .Take(TopProducts).ToArrayAsync(ct);

        var directory = await access.Shops(db).AsNoTracking().Select(shop => new { shop.Platform, shop.Sid, shop.Name })
            .ToArrayAsync(ct);
        string? ShopName(string shopPlatform, int sid) =>
            directory.FirstOrDefault(shop => shop.Platform == shopPlatform && shop.Sid == sid)?.Name;
        var visible = await orders.Select(order => new { order.Platform, order.Sid }).Distinct().ToArrayAsync(ct);
        var options = new OrderReportOptions(
            [.. visible.GroupBy(row => row.Sid).OrderBy(group => group.Key)
                .Select(group => new OrderListShop(group.Key, group.Select(row => ShopName(row.Platform, row.Sid))
                    .FirstOrDefault(name => name is not null)))],
            [.. visible.Select(row => row.Platform).Distinct().Order(StringComparer.Ordinal)]);

        return new OrderReportResponse(Summarize(data),
            Bucketize(data.Where(row => row.CreatedDate is not null), row => Day(row.CreatedDate), null, byKey: true),
            Bucketize(data, row => row.Sid.ToString(CultureInfo.InvariantCulture),
                row => ShopName(row.Platform, row.Sid) ?? $"店铺 #{row.Sid}", byKey: false),
            Bucketize(data, row => row.Platform, null, byKey: false),
            Bucketize(data, row => row.Status, null, byKey: false),
            Bucketize(data, row => string.IsNullOrWhiteSpace(row.Logistics) ? NoLogistics : row.Logistics.Trim(), null, byKey: false),
            [.. products.Select(p => new OrderReportProduct(p.ProductId, p.SkuCode, p.Name, p.ImageUrl, p.Quantity, p.Orders))],
            options);
    }

    private static string Day(DateOnly? date) => date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static OrderReportSummary Summarize(IReadOnlyList<OrderReportRow> rows) =>
        new(rows.Count, rows.Sum(row => row.ItemQuantity), rows.Count(row => row.Dispatched), rows.Count(row => row.Paid),
            Amounts(rows));

    private static IReadOnlyList<OrderReportAmount> Amounts(IEnumerable<OrderReportRow> rows) =>
    [
        .. rows.Where(row => row.Amount is not null)
            .GroupBy(row => string.IsNullOrWhiteSpace(row.Currency) ? NoCurrency : row.Currency.Trim())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OrderReportAmount(group.Key, group.Count(), group.Sum(row => row.Amount!.Value)))
    ];

    /// <summary>按维度汇总；日期按时间顺序，其余按订单数从多到少。</summary>
    private static IReadOnlyList<OrderReportBucket> Bucketize(IEnumerable<OrderReportRow> rows,
        Func<OrderReportRow, string> key, Func<OrderReportRow, string>? label, bool byKey)
    {
        var buckets = rows.GroupBy(key).Select(group => new OrderReportBucket(group.Key,
            label is null ? group.Key : label(group.First()), group.Count(), group.Sum(row => row.ItemQuantity), Amounts(group)));
        return [.. byKey
            ? buckets.OrderBy(bucket => bucket.Key, StringComparer.Ordinal)
            : buckets.OrderByDescending(bucket => bucket.Orders).ThenBy(bucket => bucket.Key, StringComparer.Ordinal)];
    }

    private sealed record OrderReportRow(string ErpId, int Sid, string Platform, string Status, string? Logistics,
        decimal? Amount, string? Currency, DateOnly? CreatedDate, bool Dispatched, bool Paid, long ItemQuantity);
}
