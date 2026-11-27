using OpenERP.Core.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSnapshotContractTests
{
    [Fact]
    public void Quantity_is_the_wide_sum_of_source_lines_without_status_deductions()
    {
        // Given 包含禁用、追加、未知标志及零数量行的来源订单，并关联两张平台单。
        var order = new SourceOrderSnapshot("same-id", "同号订单", new("source", "platform", "30", "店铺30"), "退款",
            [new("po-1", null, null), new("po-2", null, null)],
            [new("1", null, null, "sku-1", int.MaxValue, true, false),
             new("2", null, null, "sku-2", 3, false, true),
             new("3", null, null, null, 0, null, null)]);

        // When 读取当前商品件数；Then 原值只相加一次，既不按状态扣减，也不按平台单数翻倍。
        Assert.Equal((long)int.MaxValue + 3, order.ItemQuantity);
        Assert.Null(order.Items[2].Disabled);
    }

    [Fact]
    public void Shop_keys_separate_sources_platforms_and_reserved_characters()
    {
        // Given 店铺编号相同或各身份片段包含分隔符。
        var shops = new[]
        {
            new OrderShop("a", "p", "30", "同名店铺"), new OrderShop("b", "p", "30", "同名店铺"),
            new OrderShop("a", "q", "30", "同名店铺"), new OrderShop("a/p", "q", "30", "同名店铺"),
            new OrderShop("a", "p/q", "30", "同名店铺")
        };

        // When 构造筛选键；Then 身份不同就不会碰撞，名称变化不改变身份。
        Assert.Equal(5, shops.Select(shop => shop.Key).Distinct().Count());
        Assert.Equal(shops[0].Key, (shops[0] with { Label = "改名" }).Key);
    }
}
