using System.Security.Cryptography;

namespace OpenERP.Api.Orders;

internal static class OrderSnapshotFingerprint
{
    internal static string Compute(OrderSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteString(writer, snapshot.ErpId);
        WriteString(writer, snapshot.OrderNumber);
        writer.Write(snapshot.Sid);
        WriteString(writer, snapshot.Platform);
        WriteString(writer, snapshot.Status);
        WriteTime(writer, snapshot.Times.Created);
        WriteTime(writer, snapshot.Times.Paid);
        WriteTime(writer, snapshot.Times.Dispatched);
        WriteTime(writer, snapshot.Times.Refunded);

        writer.Write(snapshot.PlatformOrders.Count);
        foreach (var platformOrder in snapshot.PlatformOrders.OrderBy(order => order.PoNumber, StringComparer.Ordinal))
        {
            WriteString(writer, platformOrder.PoNumber);
            writer.Write(platformOrder.Sid);
            WriteString(writer, platformOrder.StatusCode);
            WriteString(writer, platformOrder.StatusText);
        }

        writer.Write(snapshot.Lines.Count);
        // OrderStore 按完整商品行比较多重集，排序后逐行写入以保留重复次数。
        var sortedLines = snapshot.Lines
            .OrderBy(line => line.ProductId, StringComparer.Ordinal)
            .ThenBy(line => line.Oguid, StringComparer.Ordinal)
            .ThenBy(line => line.SkuId, StringComparer.Ordinal)
            .ThenBy(line => line.SkuCode, StringComparer.Ordinal)
            .ThenBy(line => line.Quantity)
            .ThenBy(line => line.Disabled)
            .ThenBy(line => line.Appended);
        foreach (var line in sortedLines)
        {
            WriteString(writer, line.ProductId);
            WriteString(writer, line.Oguid);
            WriteString(writer, line.SkuId);
            WriteString(writer, line.SkuCode);
            writer.Write(line.Quantity);
            WriteNullableBoolean(writer, line.Disabled);
            WriteNullableBoolean(writer, line.Appended);
        }

        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteTime(BinaryWriter writer, OrderTimeValue? value)
    {
        WriteString(writer, value?.Raw);
        WriteString(writer, value?.Source);
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        // 长度标记区分 null 与空字符串，逐个写入 UTF-16 代码单元以保留序数相等语义。
        writer.Write(value?.Length ?? -1);
        if (value is null)
            return;

        foreach (var character in value)
            writer.Write((ushort)character);
    }

    private static void WriteNullableBoolean(BinaryWriter writer, bool? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value);
    }
}
