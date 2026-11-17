using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.ExternalAcceptance.Tests;

public sealed class OrderLiveAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Sid644928HasSameOrderIdsAndChildOwnershipAcrossPageSizes()
    {
        // Given 可读取真实历史订单的测试 Token。
        var settings = RequireTestSettings("OPENERP_TEST_ERP_TOKEN");
        using var http = CreateHttp();
        var erp = CreateErp(http, settings["OPENERP_TEST_ERP_TOKEN"]);
        var ct = TestContext.Current.CancellationToken;

        // When 使用同一订单读取器按两种每页数量扫完整列表。
        var small = await erp.ReadOrderIndexAsync(644928, 20, null, ct);
        if (small.IsErr)
            throw new XunitException(small.Error.Message);
        var large = await erp.ReadOrderIndexAsync(644928, 100, null, ct);
        if (large.IsErr)
            throw new XunitException(large.Error.Message);

        // Then 全量 ID 一致，源端统计吻合，已通过校验的 PO 均属于目标店铺。
        Assert.True(small.Value.Total > 100, "该店需有超过 100 单才能验证两种页长的翻页。");
        Assert.Equal(small.Value.Total, small.Value.Ids.Distinct().Count());
        Assert.Equal(large.Value.Total, large.Value.Ids.Distinct().Count());
        Assert.True(small.Value.Ids.SequenceEqual(large.Value.Ids), "两次全量扫描的 ERP ID 不一致。");
        Assert.Equal(small.Value.SkipReasons.OrderBy(pair => pair.Key), large.Value.SkipReasons.OrderBy(pair => pair.Key));
        Assert.Equal(small.Value.Total, small.Value.Orders.Count + small.Value.SkipReasons.Values.Sum());
        Assert.Equal(large.Value.Total, large.Value.Orders.Count + large.Value.SkipReasons.Values.Sum());
        Assert.True(ReadPos(small.Value).SequenceEqual(ReadPos(large.Value)), "两次全量扫描的平台子单集合不一致。");
        output.WriteLine(
            $"SID 644928 实时订单 {small.Value.Total}，两种页长 ID 与子单集合一致，跳过 {small.Value.SkipReasons.Values.Sum()}。");
        foreach (var (reason, count) in small.Value.SkipReasons)
            output.WriteLine($"跳过 {count} 单，{reason}。");
    }

    [Fact]
    public async Task Sid30StoredOrdersMatchErpDetails()
    {
        // Given 已在浏览器完成 SID 30 的两次手动同步。
        var settings = RequireTestSettings("OPENERP_TEST_ERP_TOKEN", "OPENERP_TEST_POSTGRES");
        using var http = CreateHttp();
        var ct = TestContext.Current.CancellationToken;
        var source = await CreateErp(http, settings["OPENERP_TEST_ERP_TOKEN"]).ReadOrdersAsync(30, null, ct);
        if (source.IsErr)
            throw new XunitException(source.Error.Message);
        Assert.NotEmpty(source.Value.Orders);
        var acceptedIds = source.Value.Orders.Select(order => order.ErpId).ToArray();

        // When 直接读取 PostgreSQL 三张表中的本轮有效订单。
        await using var db = new NpgsqlConnection(settings["OPENERP_TEST_POSTGRES"]);
        await db.OpenAsync(ct);
        var parentRows = await QueryAsync(db, """
            SELECT "ErpId", "OrderNumber", "Sid", "Platform", "Status", "CreatedRaw", "CreatedSource", "PaidRaw", "PaidSource",
                   "DispatchedRaw", "DispatchedSource", "RefundedRaw", "RefundedSource"
            FROM "Orders" WHERE "ErpId" = ANY(@erpIds)
            """, acceptedIds, ct);
        var poRows = await QueryAsync(db, """
            SELECT p."ErpId", p."PoNumber", p."Sid", p."StatusCode", p."StatusText"
            FROM "PlatformOrders" p WHERE p."ErpId" = ANY(@erpIds)
            """, acceptedIds, ct);
        var lineRows = await QueryAsync(db, """
            SELECT l."ErpId", l."ProductId", l."Oguid", l."SkuId", l."SkuCode", l."Quantity", l."Disabled", l."Appended"
            FROM "OrderLines" l WHERE l."ErpId" = ANY(@erpIds)
            """, acceptedIds, ct);

        // Then 本轮有效订单逐单匹配，商品行出现次数一致。
        AssertRows("ERP 主单", source.Value.Orders.Select(o => Row(o.ErpId, o.OrderNumber, o.Sid, o.Platform, o.Status,
            o.Times.Created?.Raw, o.Times.Created?.Source, o.Times.Paid?.Raw, o.Times.Paid?.Source,
            o.Times.Dispatched?.Raw, o.Times.Dispatched?.Source, o.Times.Refunded?.Raw, o.Times.Refunded?.Source)), parentRows);
        AssertRows("平台 PO", source.Value.Orders.SelectMany(o => o.PlatformOrders.Select(po =>
            Row(o.ErpId, po.PoNumber, po.Sid, po.StatusCode, po.StatusText))), poRows);
        AssertRows("商品行", source.Value.Orders.SelectMany(o => o.Lines.Select(line =>
            Row(o.ErpId, line.ProductId, line.Oguid, line.SkuId, line.SkuCode, line.Quantity, line.Disabled, line.Appended))), lineRows);
        output.WriteLine(
            $"SID 30 源端 {source.Value.Total} 单，库内匹配 {parentRows.Count} 张主单、{poRows.Count} 张平台 PO、{lineRows.Count} 行商品，跳过 {source.Value.SkipReasons.Values.Sum()}。");
    }

    private static IEnumerable<(string ErpId, string PoNumber, int Sid)> ReadPos(OrderIndex index) =>
        index.Orders.SelectMany(order => order.PlatformOrders.OrderBy(po => po.PoNumber, StringComparer.Ordinal).Select(po =>
        {
            Assert.Equal(644928, po.Sid);
            return (order.ErpId, po.PoNumber, po.Sid);
        }));

    private static HttpClient CreateHttp() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static WxwErpClient CreateErp(HttpClient http, string token) => new(http, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp:AccessToken"] = token
        }).Build());

    private static Dictionary<string, string> RequireTestSettings(params string[] names)
    {
        var settings = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var missing = settings.Where(setting => string.IsNullOrWhiteSpace(setting.Value)).Select(setting => setting.Key);
        var missingNames = missing.ToArray();
        Assert.True(missingNames.Length == 0, $"缺少必需环境变量：{string.Join("、", missingNames)}。");
        return settings.ToDictionary(setting => setting.Key, setting => setting.Value!);
    }

    private static async Task<List<string>> QueryAsync(NpgsqlConnection db, string sql, string[] acceptedIds, CancellationToken ct)
    {
        var rows = new List<string>();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("erpIds", acceptedIds);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(Row(values));
        }
        return rows;
    }

    private static string Row(params object?[] fields) => string.Concat(fields.Select(field =>
    {
        var value = field is DBNull or null ? null : Convert.ToString(field, CultureInfo.InvariantCulture);
        return value is null ? "-;" : $"{value.Length}:{value}";
    }));

    private static void AssertRows(string table, IEnumerable<string> expected, List<string> actual)
    {
        Assert.True(expected.Order(StringComparer.Ordinal).SequenceEqual(actual.Order(StringComparer.Ordinal)),
            $"{table}的数量或内容与源数据不一致。");
    }
}
