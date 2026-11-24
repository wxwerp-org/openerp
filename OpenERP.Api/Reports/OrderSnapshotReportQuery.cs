using System.Data;
using System.Data.Common;
using System.Globalization;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpResult;
using OpenERP.Api.Orders;
using OpenERP.Core.Orders;
using OpenERP.Core.Reports;

namespace OpenERP.Api.Reports;

/// <summary>从现有本地订单存储读取快照报表。</summary>
public sealed class OrderSnapshotReportQuery(AppDbContext db, TimeProvider clock,
    ILogger<OrderSnapshotReportQuery>? logger = null) : IOrderSnapshotReportQuery
{
    public async Task<OpResult<OrderSnapshotReport>> QueryAsync(OrderSnapshotReportRequest request, CancellationToken cancellationToken)
    {
        var generatedAt = clock.GetUtcNow().UtcDateTime;
        var trendEnd = request.TrendEnd ?? DateOnly.FromDateTime(generatedAt);
        if (request.TrendEnd is null)
            request = request with { TrendEnd = trendEnd };
        if (request.ValidationError is { } error)
            return OpResults.Err(error);

        try
        {
            // 同步可能在查询期间替换商品行；同一事务保证汇总、选项和明细来自同一快照。
            // 执行策略可以在瞬时故障后重试整个读取事务。
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                var report = await ReadAsync(request, generatedAt, trendEnd, ct);
                await transaction.CommitAsync(ct);
                return OpResults.Ok(report);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or RetryLimitExceededException)
        {
            logger?.LogError(exception, "读取本地订单商品量快照失败。");
            return OpResults.Err("读取本地订单失败，请重试。");
        }
    }

    private async Task<OrderSnapshotReport> ReadAsync(
        OrderSnapshotReportRequest request, DateTime generatedAt, DateOnly trendEnd, CancellationToken ct)
    {
        var all = db.Orders.AsNoTracking();
        if (request.AllowedShops is { } allowed)
            all = FilterByShops(all, allowed);
        var identities = await all.Select(order => new { order.Platform, order.Sid }).Distinct()
            .OrderBy(shop => shop.Platform).ThenBy(shop => shop.Sid).ToArrayAsync(ct);
        var shops = identities.Select(shop => StoredOrderMapping.Shop(shop.Platform, shop.Sid)).ToArray();
        var selected = all;
        if (!string.IsNullOrEmpty(request.Platform))
            selected = selected.Where(order => order.Platform == request.Platform);
        if (request.Shops.Count > 0)
        {
            // 键包含来源和平台，不把相同 SID 的不同店铺合并；未知键产生空结果。
            var selectedKeys = request.Shops.ToHashSet(StringComparer.Ordinal);
            selected = FilterByShops(selected, shops.Where(shop => selectedKeys.Contains(shop.Key)));
        }

        // 分别聚合订单与商品，避免联接多个平台单后把商品数量乘算。
        var counts = await selected.GroupBy(order => new { order.Platform, order.Sid })
            .Select(group => new { group.Key.Platform, group.Key.Sid, Count = group.LongCount() })
            .OrderBy(group => group.Platform).ThenBy(group => group.Sid).ToArrayAsync(ct);
        var quantities = await selected.SelectMany(order => order.Lines,
                (order, line) => new { order.Platform, order.Sid, line.Quantity })
            .GroupBy(line => new { line.Platform, line.Sid })
            .Select(group => new { group.Key.Platform, group.Key.Sid, Quantity = group.Sum(line => (long)line.Quantity) })
            .ToArrayAsync(ct);
        var quantityByShop = quantities.ToDictionary(group => (group.Platform, group.Sid), group => group.Quantity);
        var groups = counts.Select(group => new OrderShopTotals(StoredOrderMapping.Shop(group.Platform, group.Sid),
            group.Count, quantityByShop.GetValueOrDefault((group.Platform, group.Sid)))).ToArray();
        var summary = new OrderSnapshotTotals(groups.Sum(group => group.OrderCount), groups.Sum(group => group.ItemQuantity));
        var pageCount = (int)Math.Max(1, (summary.OrderCount + request.PageSize - 1) / request.PageSize);
        var page = Math.Min(request.Page, pageCount);
        var rows = await selected.OrderBy(order => order.Platform).ThenBy(order => order.Sid).ThenBy(order => order.ErpId)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize)
            .Include(order => order.PlatformOrders).Include(order => order.Lines)
            .AsSplitQuery().ToArrayAsync(ct);

        var (sales, trend) = await ReadAmountsAsync(selected, request.TrendStart, trendEnd, ct);
        return new(generatedAt, summary, shops.Select(shop => shop.Platform).Distinct().ToArray(),
            shops, groups, rows.Select(StoredOrderMapping.Snapshot).ToArray(), page, request.PageSize, pageCount, sales, trend);
    }

    private async Task<(OrderSnapshotSales Sales, OrderSnapshotTrend Trend)> ReadAmountsAsync(
        IQueryable<OrderRecord> selected, DateOnly? start, DateOnly end, CancellationToken ct)
    {
        var lastDay = end.DayNumber;
        var firstDay = start?.DayNumber ?? lastDay - 6;
        var days = lastDay - firstDay + 1;
        var previousFirst = firstDay - days;
        var buckets = await QueryAmountBuckets(selected, previousFirst, lastDay).ToArrayAsync(ct);

        var known = buckets.Sum(bucket => bucket.KnownCount);
        var sales = new OrderSnapshotSales(known == 0 ? null : buckets.Sum(bucket => bucket.Amount),
            CurrencyOf(buckets), known, buckets.Sum(bucket => bucket.OrderCount) - known);

        var current = buckets.Where(bucket => bucket.Day >= firstDay && bucket.Day <= lastDay).ToLookup(bucket => bucket.Day);
        var currentKnown = current.SelectMany(group => group).Sum(bucket => bucket.KnownCount);
        var points = Enumerable.Range(firstDay, days).Select(day =>
        {
            var totals = SumWindow(current[day]);
            return new OrderTrendPoint(FormatDay(day), totals.OrderCount, totals.ItemQuantity, totals.SalesAmount);
        }).ToArray();

        var previous = buckets.Any(bucket => bucket.Day < firstDay)
            ? SumWindow(buckets.Where(bucket => bucket.Day >= previousFirst && bucket.Day < firstDay))
            : null;
        var trend = new OrderSnapshotTrend(days, points[0].Date, end, CurrencyOf(current.SelectMany(group => group)), points, previous,
            buckets.Where(bucket => bucket.Day == null).Sum(bucket => bucket.OrderCount),
            currentKnown, points.Sum(point => point.OrderCount) - currentKnown);
        return (sales, trend);
    }

    private static IQueryable<AmountBucket> QueryAmountBuckets(IQueryable<OrderRecord> selected, int previousFirst, int lastDay)
    {
        return (from order in selected
                    let day = order.CreatedAtTicks >= 0 ? (int?)(order.CreatedAtTicks / TimeSpan.TicksPerDay) : null
                    select new
                    {
                        // 窗口外按前后汇总，供销售额和历史判定使用。
                        Day = day < previousFirst ? previousFirst - 1 : day > lastDay ? lastDay + 1 : day,
                        Currency = order.IncomeCost != null ? order.IncomeCurrency : null,
                        order.IncomeCost,
                        Quantity = day >= previousFirst && day <= lastDay
                            ? order.Lines.Sum(line => (long)line.Quantity) : 0L
                    })
            .GroupBy(row => new { row.Day, row.Currency })
            .Select(group => new AmountBucket(group.Key.Day, group.Key.Currency, group.LongCount(),
                group.LongCount(row => row.IncomeCost != null), group.Sum(row => row.Quantity),
                group.Sum(row => row.IncomeCost) ?? 0m));
    }

    private static OrderTrendPrevious SumWindow(IEnumerable<AmountBucket> buckets)
    {
        long orders = 0;
        long quantity = 0;
        long known = 0;
        decimal amount = 0;
        foreach (var bucket in buckets)
        {
            orders += bucket.OrderCount;
            quantity += bucket.ItemQuantity;
            known += bucket.KnownCount;
            amount += bucket.Amount;
        }

        return new(orders, quantity, orders > 0 && known == 0 ? null : amount);
    }

    private static string? CurrencyOf(IEnumerable<AmountBucket> buckets)
    {
        var currencies = buckets.Where(bucket => bucket.KnownCount > 0)
            .Select(bucket => bucket.Currency).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return currencies.Length == 1 ? currencies[0] : null;
    }

    // 省略开始日期且结束日期早于 0001-01-07 时，默认窗口向前延伸至 ISO 8601 的 0000 年 12 月。
    private static string FormatDay(int day) => day < 0
        ? FormattableString.Invariant($"0000-12-{32 + day:00}")
        : DateOnly.FromDayNumber(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record AmountBucket(int? Day, string? Currency, long OrderCount, long KnownCount,
        long ItemQuantity, decimal Amount);
    private static IQueryable<OrderRecord> FilterByShops(IQueryable<OrderRecord> orders, IEnumerable<OrderShop> shops)
    {
        var keys = shops.Select(shop => shop.Platform + "/" + shop.ShopId).ToArray();
        return orders.Where(order => keys.Contains(order.Platform + "/" + order.Sid.ToString()));
    }
}

/// <summary>现有 ERP 专用存储到公共订单事实的映射；不把主单时间或状态推断成平台事实。</summary>
internal static class StoredOrderMapping
{
    internal static OrderShop Shop(string platform, int sid) =>
        new("wxw-erp", platform, sid.ToString(CultureInfo.InvariantCulture), $"店铺 {sid.ToString(CultureInfo.InvariantCulture)}");

    private static readonly TypeAdapterConfig Mappings = CreateMappings();

    internal static SourceOrderSnapshot Snapshot(OrderRecord order) =>
        order.Adapt<SourceOrderSnapshot>(Mappings);

    private static TypeAdapterConfig CreateMappings()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<PlatformOrderRecord, PlatformOrderReference>()
            .Map(target => target.Number, source => source.PoNumber);
        config.NewConfig<OrderLineRecord, OrderItemSnapshot>()
            .Map(target => target.LineId, source => source.Ordinal.ToString(CultureInfo.InvariantCulture));
        config.NewConfig<OrderRecord, SourceOrderSnapshot>()
            .Map(target => target.Id, source => source.ErpId)
            .Map(target => target.Number, source => source.OrderNumber)
            .Map(target => target.Shop, source => Shop(source.Platform, source.Sid))
            .Map(target => target.PlatformOrders,
                source => source.PlatformOrders.OrderBy(po => po.PoNumber, StringComparer.Ordinal))
            .Map(target => target.Items, source => source.Lines.OrderBy(line => line.Ordinal));
        return config;
    }
}
