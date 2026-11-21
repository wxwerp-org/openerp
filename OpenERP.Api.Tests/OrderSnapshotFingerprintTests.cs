using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSnapshotFingerprintTests
{
    private static readonly OrderTimeValue Time = new("2026-11-20 13:14:15", "ERP");
    private static readonly PlatformOrderSnapshot PlatformOrder = new("PO", 12, "new", "待处理");
    private static readonly OrderLineSnapshot Line = new("product", "oguid", "sku-id", "sku-code", 2, false, true);

    private static OrderSnapshot CreateSnapshot() =>
        new("erp-1", "order-1", 11, "platform", "active", new(Time, Time, Time, Time), [PlatformOrder], [Line]);

    [Fact]
    public void Changing_any_persisted_field_changes_the_fingerprint()
    {
        // Given 一张含完整主单、四种时间、平台单及商品的订单。
        var snapshot = CreateSnapshot();
        var changes = new OrderSnapshot[]
        {
            snapshot with { ErpId = "erp-2" },
            snapshot with { OrderNumber = "order-2" },
            snapshot with { Sid = 21 },
            snapshot with { Platform = "Platform" },
            snapshot with { Status = "closed" },
            snapshot with { IncomeCost = 0m },
            snapshot with { IncomeCurrency = "USD" },
            snapshot with { Times = snapshot.Times with { Created = Time with { Raw = "other" } } },
            snapshot with { Times = snapshot.Times with { Created = Time with { Source = "other" } } },
            snapshot with { Times = snapshot.Times with { Paid = Time with { Raw = "other" } } },
            snapshot with { Times = snapshot.Times with { Paid = Time with { Source = "other" } } },
            snapshot with { Times = snapshot.Times with { Dispatched = Time with { Raw = "other" } } },
            snapshot with { Times = snapshot.Times with { Dispatched = Time with { Source = "other" } } },
            snapshot with { Times = snapshot.Times with { Refunded = Time with { Raw = "other" } } },
            snapshot with { Times = snapshot.Times with { Refunded = Time with { Source = "other" } } },
            snapshot with { Times = snapshot.Times with { Refunded = null } },
            snapshot with { PlatformOrders = [PlatformOrder with { PoNumber = "po" }] },
            snapshot with { PlatformOrders = [PlatformOrder with { Sid = 22 }] },
            snapshot with { PlatformOrders = [PlatformOrder with { StatusCode = "old" }] },
            snapshot with { PlatformOrders = [PlatformOrder with { StatusText = "已处理" }] },
            snapshot with { PlatformOrders = [] },
            snapshot with { Lines = [Line with { ProductId = "product-2" }] },
            snapshot with { Lines = [Line with { Oguid = "oguid-2" }] },
            snapshot with { Lines = [Line with { SkuId = "sku-id-2" }] },
            snapshot with { Lines = [Line with { SkuCode = "sku-code-2" }] },
            snapshot with { Lines = [Line with { Quantity = 3 }] },
            snapshot with { Lines = [Line with { Disabled = true }] },
            snapshot with { Lines = [Line with { Appended = false }] },
            snapshot with { Lines = [Line with { Appended = null }] },
            snapshot with { Lines = [] }
        };

        // When 分别改动一个字段或删除一个子项。
        var fingerprint = OrderSnapshotFingerprint.Compute(snapshot);

        // Then 每一种改动都使指纹变化。
        Assert.All(changes, change => Assert.NotEqual(fingerprint, OrderSnapshotFingerprint.Compute(change)));
    }

    [Fact]
    public void Duplicate_line_counts_change_the_fingerprint()
    {
        // Given 相同的商品成员和行数，只有各成员的重复次数不同。
        var otherLine = Line with { Quantity = 3 };
        var snapshot = CreateSnapshot();
        var original = snapshot with { Lines = [Line, Line, otherLine] };
        var changed = snapshot with { Lines = [Line, otherLine, otherLine] };

        // When 计算两张快照的指纹；Then 重复次数的差异使指纹变化。
        Assert.NotEqual(OrderSnapshotFingerprint.Compute(original), OrderSnapshotFingerprint.Compute(changed));
    }

    [Fact]
    public void Reordering_platform_orders_preserves_the_fingerprint()
    {
        // Given 平台单号按序数字符区分，包括语言排序可能视为相同的不同拼写。
        var snapshot = CreateSnapshot() with
        {
            PlatformOrders = [PlatformOrder with { PoNumber = "\u00E9" }, PlatformOrder with { PoNumber = "e\u0301" }]
        };

        // When 调换平台单顺序；Then 指纹相同。
        var reordered = snapshot with { PlatformOrders = [.. snapshot.PlatformOrders.Reverse()] };
        Assert.Equal(OrderSnapshotFingerprint.Compute(snapshot), OrderSnapshotFingerprint.Compute(reordered));
    }

    [Fact]
    public void Reordering_lines_with_duplicates_preserves_the_fingerprint()
    {
        // Given 重复行以及只在一个字段上不同的商品行。
        var snapshot = CreateSnapshot() with
        {
            Lines =
            [
                Line,
                Line with { ProductId = null },
                Line with { Oguid = "\u00E9" },
                Line with { Oguid = "e\u0301" },
                Line with { SkuId = "sku-id-2" },
                Line with { SkuCode = "sku-code-2" },
                Line with { Quantity = 3 },
                Line with { Disabled = true },
                Line with { Appended = null },
                Line
            ]
        };

        // When 调换商品行顺序；Then 指纹相同。
        var reordered = snapshot with { Lines = [.. snapshot.Lines.Reverse()] };
        Assert.Equal(OrderSnapshotFingerprint.Compute(snapshot), OrderSnapshotFingerprint.Compute(reordered));
    }

    [Fact]
    public void Null_and_empty_strings_change_the_fingerprint()
    {
        // Given 平台状态与商品编号为空值。
        var snapshot = CreateSnapshot() with
        {
            PlatformOrders = [PlatformOrder with { StatusCode = null }],
            Lines = [Line with { ProductId = null }]
        };

        // When 分别将两个空值替换为空字符串。
        var emptyStatusCode = snapshot with { PlatformOrders = [PlatformOrder with { StatusCode = "" }] };
        var emptyProductId = snapshot with { Lines = [Line with { ProductId = "" }] };

        // Then 两种替换都使指纹变化。
        var fingerprint = OrderSnapshotFingerprint.Compute(snapshot);
        Assert.NotEqual(fingerprint, OrderSnapshotFingerprint.Compute(emptyStatusCode));
        Assert.NotEqual(fingerprint, OrderSnapshotFingerprint.Compute(emptyProductId));
    }

    [Fact]
    public void String_boundaries_are_preserved_in_the_fingerprint()
    {
        // Given 相邻文本字段拼接相同，但分隔符归属不同。
        var snapshot = CreateSnapshot() with { Platform = "ab", Status = "active|closed" };
        var different = snapshot with { Platform = "ab|active", Status = "closed" };

        // When 计算指纹；Then 字段边界的差异使指纹变化。
        Assert.NotEqual(OrderSnapshotFingerprint.Compute(snapshot), OrderSnapshotFingerprint.Compute(different));
    }

    [Fact]
    public void Fingerprint_is_a_stable_sha256_hex_string()
    {
        // Given 两张独立构造的相同订单快照。
        var original = CreateSnapshot();
        var equivalent = CreateSnapshot();

        // When 分别计算指纹。
        var fingerprint = OrderSnapshotFingerprint.Compute(original);

        // Then 两次结果一致，采用 64 个十六进制字符表示 SHA-256。
        Assert.Matches("^[0-9A-Fa-f]{64}$", fingerprint);
        Assert.Equal(fingerprint, OrderSnapshotFingerprint.Compute(equivalent));
    }
}
