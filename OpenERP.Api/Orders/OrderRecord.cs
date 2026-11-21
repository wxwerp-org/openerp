namespace OpenERP.Api.Orders;

internal sealed class OrderRecord : Entity
{
    public required string ErpId { get; init; }
    public required string OrderNumber { get; set; }
    public int Sid { get; set; }
    public required string Platform { get; set; }
    public required string Status { get; set; }
    public Guid SyncRevision { get; set; }
    public decimal? IncomeCost { get; set; }
    public string? IncomeCurrency { get; set; }
    public string? CreatedRaw { get; set; }
    public string? CreatedSource { get; set; }
    public string? PaidRaw { get; set; }
    public string? PaidSource { get; set; }
    public string? DispatchedRaw { get; set; }
    public string? DispatchedSource { get; set; }
    public string? RefundedRaw { get; set; }
    public string? RefundedSource { get; set; }
    public List<PlatformOrderRecord> PlatformOrders { get; } = [];
    public List<OrderLineRecord> Lines { get; } = [];
}

internal sealed class PlatformOrderRecord : Entity
{
    public required string ErpId { get; init; }
    public required string PoNumber { get; init; }
    public int Sid { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusText { get; set; }
}

internal sealed class OrderLineRecord : Entity
{
    public required string ErpId { get; init; }
    public int Ordinal { get; init; }
    public string? ProductId { get; set; }
    public string? Oguid { get; set; }
    public string? SkuId { get; set; }
    public string? SkuCode { get; set; }
    public int Quantity { get; set; }
    public bool? Disabled { get; set; }
    public bool? Appended { get; set; }
}
