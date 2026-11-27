namespace OpenERP.Api.Orders;

public sealed record OrderSnapshot(string ErpId, string OrderNumber, int Sid, string Platform, string Status,
    OrderTimes Times, IReadOnlyList<PlatformOrderSnapshot> PlatformOrders, IReadOnlyList<OrderLineSnapshot> Lines,
    decimal? IncomeCost = null, string? IncomeCurrency = null, string? Buyer = null, decimal? TotalAmount = null,
    string? TotalCurrency = null, string? Logistics = null, string? Details = null);

public sealed record PlatformOrderSnapshot(string PoNumber, int Sid, string? StatusCode, string? StatusText);

public sealed record OrderLineSnapshot(string? ProductId, string? Oguid, string? SkuId, string? SkuCode,
    int Quantity, bool? Disabled, bool? Appended, string? Name = null, string? ImageUrl = null,
    string? Specs = null, decimal? UnitPrice = null, string? Currency = null, string? Logistics = null,
    string? Details = null);

/// <summary>
/// 平台无关的补充信息条目（名称 + 取值）。快照里以 JSON 数组文本保存（<see cref="OrderSnapshot.Details"/>），
/// 各平台来源自行决定哪些字段值得展示，订单列表按条目原样输出。
/// </summary>
public sealed record OrderDetail(string Label, string Value, string Group = "other");

public sealed record OrderTimes(OrderTimeValue? Created, OrderTimeValue? Paid, OrderTimeValue? Dispatched,
    OrderTimeValue? Refunded);

public sealed record OrderTimeValue(string Raw, string Source);

public sealed record OrderReadProgress(string Stage, int Total, int Read, int Processed);

public sealed record OrderImportBatch(int Total, IReadOnlyList<OrderSnapshot> Orders,
    IReadOnlyDictionary<string, int> SkipReasons);
