namespace OpenERP.Core.Orders;

/// <summary>来源内的店铺身份；跨来源、平台的同名店铺标识互不覆盖。</summary>
public sealed record OrderShop(string Source, string Platform, string ShopId, string Label)
{
    public string Key => string.Join('/', Uri.EscapeDataString(Source),
        Uri.EscapeDataString(Platform), Uri.EscapeDataString(ShopId));
}

/// <summary>来源订单关联的平台单，仅作为参考，不代表已建立商品归属。</summary>
public sealed record PlatformOrderReference(string Number, string? StatusCode, string? StatusText);

/// <summary>当前商品行事实；未知标志保留 null，数量不根据状态推断或改写。</summary>
public sealed record OrderItemSnapshot(string LineId, string? ProductId, string? SkuId, string? SkuCode,
    int Quantity, bool? Disabled, bool? Appended);

/// <summary>本地来源订单的当前事实，不假定一个来源订单等于一个平台订单。</summary>
public sealed record SourceOrderSnapshot(string Id, string Number, OrderShop Shop, string Status,
    IReadOnlyList<PlatformOrderReference> PlatformOrders, IReadOnlyList<OrderItemSnapshot> Items)
{
    public long ItemQuantity => Items.Sum(item => (long)item.Quantity);
}
