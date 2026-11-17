using System.Globalization;
using System.Text.Json;
using Npgsql;
using OpenERP.Api.Erp;

internal sealed record DatabaseRows(List<Dictionary<string, object?>> Orders,
    List<Dictionary<string, object?>> PlatformOrders, List<Dictionary<string, object?>> OrderLines);

internal static class OrderDatabase
{
    internal static async Task<DatabaseRows> ReadAsync(string connection, int sid, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(ct);
        return new(
            await QueryAsync(db, """
                SELECT "ErpId", "OrderNumber", "Sid", "Platform", "Status", "CreatedRaw", "CreatedSource",
                       "PaidRaw", "PaidSource", "DispatchedRaw", "DispatchedSource", "RefundedRaw", "RefundedSource"
                FROM "Orders" WHERE "Sid" = @sid
                """, sid, ct),
            await QueryAsync(db, """
                SELECT p."ErpId", p."PoNumber", p."Sid", p."StatusCode", p."StatusText"
                FROM "PlatformOrders" p JOIN "Orders" o ON o."ErpId" = p."ErpId" WHERE o."Sid" = @sid
                """, sid, ct),
            await QueryAsync(db, """
                SELECT l."ErpId", l."ProductId", l."Oguid", l."SkuId", l."SkuCode", l."Quantity", l."Disabled", l."Appended"
                FROM "OrderLines" l JOIN "Orders" o ON o."ErpId" = l."ErpId" WHERE o."Sid" = @sid
                """, sid, ct));
    }

    // 真实 wxw 模式沿用原对账范围，但同步动作现在由 Playwright 自动完成。
    internal static async Task<object> VerifyLiveAsync(string connection, string token, int sid, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(15) };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Erp:AccessToken"] = token }).Build();
        var source = await new WxwErpClient(http, config).ReadOrdersAsync(sid, null, ct);
        if (source.IsErr) return new { errors = new[] { source.Error.Message } };
        var actual = await ReadAsync(connection, sid, ct);
        var expected = source.Value.Orders;
        var errors = new List<string>();
        if (expected.Count == 0) errors.Add("wxw returned no accepted orders; this is not a successful sync verification.");
        Compare("Orders", expected.Select(o => Row(o.ErpId, o.OrderNumber, o.Sid, o.Platform, o.Status,
            o.Times.Created?.Raw, o.Times.Created?.Source, o.Times.Paid?.Raw, o.Times.Paid?.Source,
            o.Times.Dispatched?.Raw, o.Times.Dispatched?.Source, o.Times.Refunded?.Raw, o.Times.Refunded?.Source)),
            actual.Orders, errors);
        Compare("PlatformOrders", expected.SelectMany(o => o.PlatformOrders.Select(p =>
            Row(o.ErpId, p.PoNumber, p.Sid, p.StatusCode, p.StatusText))), actual.PlatformOrders, errors);
        Compare("OrderLines", expected.SelectMany(o => o.Lines.Select(l =>
            Row(o.ErpId, l.ProductId, l.Oguid, l.SkuId, l.SkuCode, l.Quantity, l.Disabled, l.Appended))), actual.OrderLines, errors);
        return new { total = source.Value.Total, orders = actual.Orders.Count,
            platformOrders = actual.PlatformOrders.Count, orderLines = actual.OrderLines.Count, errors };
    }

    private static void Compare(string table, IEnumerable<string> expected,
        List<Dictionary<string, object?>> actual, List<string> errors)
    {
        if (!expected.Order(StringComparer.Ordinal).SequenceEqual(
            actual.Select(row => Row(row.Values.ToArray())).Order(StringComparer.Ordinal)))
            errors.Add($"{table}: row multiplicities or field values differ from wxw.");
    }

    private static string Row(params object?[] fields) => string.Concat(fields.Select(field =>
    {
        var value = field is null ? null : Convert.ToString(field, CultureInfo.InvariantCulture);
        return value is null ? "-;" : $"{value.Length}:{value}";
    }));

    private static async Task<List<Dictionary<string, object?>>> QueryAsync(NpgsqlConnection db, string sql, int sid, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("sid", sid);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[JsonNamingPolicy.CamelCase.ConvertName(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
