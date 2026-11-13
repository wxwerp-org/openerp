namespace OpenERP.Api.Orders;

public sealed record OrderSnapshot(string ErpId, string OrderNumber, int Sid, string Platform, string Status,
    OrderTimes Times, IReadOnlyList<PlatformOrderSnapshot> PlatformOrders, IReadOnlyList<OrderLineSnapshot> Lines);

public sealed record PlatformOrderSnapshot(string PoNumber, int Sid, string? StatusCode, string? StatusText);

public sealed record OrderLineSnapshot(string? ProductId, string? Oguid, string? SkuId, string? SkuCode,
    int Quantity, bool? Disabled, bool? Appended);

public sealed record OrderTimes(OrderTimeValue? Created, OrderTimeValue? Paid, OrderTimeValue? Dispatched,
    OrderTimeValue? Refunded);

public sealed record OrderTimeValue(string Raw, string Source);

public sealed record OrderReadProgress(string Stage, int Total, int Read, int Processed);

public sealed record OrderImportBatch(int Total, IReadOnlyList<OrderSnapshot> Orders,
    IReadOnlyDictionary<string, int> SkipReasons);

public sealed record OrderIndex(int Total, IReadOnlyList<string> Ids, IReadOnlyList<ErpOrderHeader> Orders,
    IReadOnlyDictionary<string, int> SkipReasons);

public sealed record ErpOrderHeader(string ErpId, string OrderNumber, OrderTimes Times,
    IReadOnlyList<PlatformOrderSnapshot> PlatformOrders);
