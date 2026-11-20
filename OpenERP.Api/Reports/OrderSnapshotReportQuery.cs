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
        if (request.ValidationError is { } error)
            return OpResults.Err(error);

        try
        {
            // 同步可能在查询期间替换商品行；同一事务保证汇总、选项和明细来自同一快照。
            // 将事务放入既有执行策略内，兼容 Aspire/Npgsql 配置的瞬时故障重试。
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                var report = await ReadAsync(request, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OpResults.Ok(report);
            });
        }
        catch (Exception exception) when (exception is DbException or RetryLimitExceededException)
        {
            logger?.LogError(exception, "读取本地订单商品量快照失败。");
            return OpResults.Err("读取本地订单失败，请重试。");
        }
    }

    private async Task<OrderSnapshotReport> ReadAsync(OrderSnapshotReportRequest request, CancellationToken ct)
    {
        var all = db.Orders.AsNoTracking();
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
            var storeKeys = shops.Where(shop => selectedKeys.Contains(shop.Key))
                .Select(shop => shop.Platform + "/" + shop.ShopId).ToArray();
            selected = selected.Where(order => storeKeys.Contains(order.Platform + "/" + order.Sid.ToString()));
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

        return new(clock.GetUtcNow().UtcDateTime, summary, shops.Select(shop => shop.Platform).Distinct().ToArray(),
            shops, groups, rows.Select(StoredOrderMapping.Snapshot).ToArray(), page, request.PageSize, pageCount);
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
