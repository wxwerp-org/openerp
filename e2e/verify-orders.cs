#:project ../OpenERP.Api/OpenERP.Api.csproj
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using OpenERP.Api.Erp;


// 只执行订单数据断言所需的查询；连接串由 Aspire 提供，不启动服务或容器。
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var connection = Environment.GetEnvironmentVariable("OPENERP_E2E_POSTGRES")
    ?? throw new InvalidOperationException("Aspire did not provide the isolated test database.");
var sid = int.Parse(args[1], CultureInfo.InvariantCulture);
object result = args[0] == "live"
    ? await OrderDatabase.VerifyLiveAsync(connection,
        Environment.GetEnvironmentVariable("OPENERP_TEST_ERP_TOKEN")
            ?? throw new InvalidOperationException("Live verification requires a wxw token."), sid, timeout.Token)
    : await OrderDatabase.ReadAsync(connection, sid, timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

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
        var orders = source.Value.Orders;
        // 仅输出数据，由 Playwright 对 stub/live 使用同一套原生深比较断言。
        var expected = new
        {
            Orders = orders.Select(o => new
            {
                o.ErpId, o.OrderNumber, o.Sid, o.Platform, o.Status,
                CreatedRaw = o.Times.Created?.Raw, CreatedSource = o.Times.Created?.Source,
                PaidRaw = o.Times.Paid?.Raw, PaidSource = o.Times.Paid?.Source,
                DispatchedRaw = o.Times.Dispatched?.Raw, DispatchedSource = o.Times.Dispatched?.Source,
                RefundedRaw = o.Times.Refunded?.Raw, RefundedSource = o.Times.Refunded?.Source,
            }),
            PlatformOrders = orders.SelectMany(o => o.PlatformOrders.Select(p => new
            { o.ErpId, p.PoNumber, p.Sid, p.StatusCode, p.StatusText })),
            OrderLines = orders.SelectMany(o => o.Lines.Select(l => new
            { o.ErpId, l.ProductId, l.Oguid, l.SkuId, l.SkuCode, l.Quantity, l.Disabled, l.Appended })),
        };
        return new { total = source.Value.Total, actual, expected, errors = Array.Empty<string>() };
    }

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
