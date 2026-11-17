using Microsoft.Extensions.Configuration;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.LiveIntegrationTests;

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

}
