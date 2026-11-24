namespace OpenERP.Api.Orders;

/// <summary>定时同步设置实体；全表最多保留一行。</summary>
public sealed class OrderSyncSettings : Entity
{
    public bool Enabled { get; set; }

    public int[] ShopSids { get; set; } = [];
}
