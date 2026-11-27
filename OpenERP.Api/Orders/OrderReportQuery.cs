using System.Globalization;

using Microsoft.EntityFrameworkCore;

using OpenERP.Api.Authentication;

namespace OpenERP.Api.Orders;

/// <summary>订单报表：在可见本地订单上按创建日期、店铺、平台等条件统计订单数、件数与按币种分列的金额。</summary>
internal sealed class OrderReportQuery(AppDbContext db)
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
        var fromTicks = request.CreatedFrom is { } from ? (long)from.DayNumber * TimeSpan.TicksPerDay : (long?)null;
        var endTicks = request.CreatedTo is { } to ? (to.DayNumber + 1L) * TimeSpan.TicksPerDay : (long?)null;
        var matched = orders.Where(order => (request.ShopSid == null || order.Sid == request.ShopSid)
            && (platform == null || order.Platform == platform)
            && (fromTicks == null || order.CreatedAtTicks >= fromTicks)
            && (endTicks == null || order.CreatedAtTicks < endTicks));
        var rawRows = await matched.Select(order => new
        {
            order.ErpId, order.Sid, order.Platform, order.Status, order.Logistics, order.TotalAmount, order.TotalCurrency,
            order.CreatedAtTicks, order.DispatchedRaw, order.PaidRaw,
            ItemQuantity = lines.Where(line => line.ErpId == order.ErpId).Sum(line => (long)line.Quantity)
        }).ToArrayAsync(ct);
        var data = rawRows.Select(row => new OrderReportRow(row.ErpId, row.Sid, row.Platform, row.Status, row.Logistics,
            row.TotalAmount, row.TotalCurrency,
            row.CreatedAtTicks is >= 0 ? DateOnly.FromDayNumber((int)(row.CreatedAtTicks.Value / TimeSpan.TicksPerDay)) : null,
            OrderSourceTime.ToTicks(row.DispatchedRaw) >= 0,
            row.PaidRaw != null && row.PaidRaw.Trim() != "", row.ItemQuantity)).ToArray();

        var ids = matched.Select(order => order.ErpId);
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
