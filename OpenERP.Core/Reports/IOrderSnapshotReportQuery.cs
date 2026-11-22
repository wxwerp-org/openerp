using OpResult;
using OpenERP.Core.Orders;

namespace OpenERP.Core.Reports;

/// <summary>本地订单快照查询；空平台、空店铺集合表示全部，店铺键来自报表选项。</summary>
public sealed record OrderSnapshotReportRequest(string? Platform, IReadOnlyList<string> Shops, int Page = 1, int PageSize = 20,
    DateOnly? TrendStart = null, DateOnly? TrendEnd = null)
{
    /// <summary>服务端计算的完整店铺读取范围；null 表示全量，空集合表示无权读取任何店铺。</summary>
    public IReadOnlyList<OrderShop>? AllowedShops { get; init; }


    public string? ValidationError => Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue
        ? "页码必须大于0，每页1至100条，分页范围不能超出限制。"
        : Shops.Count > 100 || Shops.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 1000)
            ? "请选择有效店铺，最多同时选择100家。"
            : Platform is { Length: > 100 } ? "平台标识过长。"
            : TrendStart > TrendEnd ? "走势开始日期不能晚于结束日期。"
            : TrendEnd?.DayNumber - TrendStart?.DayNumber + 1 > 366 ? "走势区间最长支持 366 天。"
            : null;
}

/// <summary>来源订单条数和当前商品行数量原值之和，不等同于付款后净销量。</summary>
public sealed record OrderSnapshotTotals(long OrderCount, long ItemQuantity);

/// <summary>按完整店铺身份汇总的快照数量。</summary>
public sealed record OrderShopTotals(OrderShop Shop, long OrderCount, long ItemQuantity);

/// <summary>单次一致读取的报表；GeneratedAtUtc 为查询时间，不是最后同步时间。</summary>
public sealed record OrderSnapshotReport(DateTime GeneratedAtUtc, OrderSnapshotTotals Summary,
    IReadOnlyList<string> Platforms, IReadOnlyList<OrderShop> Shops, IReadOnlyList<OrderShopTotals> Groups,
    IReadOnlyList<SourceOrderSnapshot> Orders, int Page, int PageSize, int PageCount, OrderSnapshotSales Sales, OrderSnapshotTrend Trend);

public sealed record OrderSnapshotSales(decimal? Amount, string? Currency, long KnownOrderCount, long UnknownOrderCount);

public sealed record OrderSnapshotTrend(int Days, string Start, DateOnly End, string? Currency, IReadOnlyList<OrderTrendPoint> Points,
    OrderTrendPrevious? Previous, long MissingCreatedCount, long AmountKnownOrderCount, long AmountUnknownOrderCount);

public sealed record OrderTrendPoint(string Date, long OrderCount, long ItemQuantity, decimal? SalesAmount);

public sealed record OrderTrendPrevious(long OrderCount, long ItemQuantity, decimal? SalesAmount);

/// <summary>报表读取边界；实现按照请求的店铺范围读取本地事实并保留来源数量。</summary>
public interface IOrderSnapshotReportQuery
{
    Task<OpResult<OrderSnapshotReport>> QueryAsync(OrderSnapshotReportRequest request, CancellationToken cancellationToken);
}
