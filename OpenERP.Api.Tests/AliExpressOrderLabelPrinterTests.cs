using System.Text.Json;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders.Printing;
using OpenERP.Api.Orders.Sources;
using OpResult;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AliExpressOrderLabelPrinterTests
{
    private static readonly PlatformShopRef Shop = new(40, AliExpressOrderSource.PlatformName, "测试店铺");
    private static readonly byte[] Pdf = "%PDF-1.4 label"u8.ToArray();

    [Fact]
    public async Task Label_is_fetched_in_two_steps_from_the_trade_order_number()
    {
        // Given 交易单已线上发货：物流订单查询返回国际运单号，面单接口返回 Base64 的 PDF。
        var agent = new FakeAgent(
            logistics: """{"result":{"success":true,"result_list":[{"international_logistics_num":"LP123","logistics_order_id":777,"trade_order_id":1000}]}}""",
            print: $$$"""{"result":"{{{Convert.ToBase64String(Pdf)}}}"}""");
        var printer = new AliExpressOrderLabelPrinter(agent, new TestErpTokenStore("test-token"));

        // When 打印交易单 1000 的面单。
        var result = await printer.PrintLabelsAsync(Shop, ["1000"], TestContext.Current.CancellationToken);

        // Then 先按交易单查物流订单，再用国际运单号取面单；只传店铺 Sid，文档内容为原始 PDF。
        var batch = Assert.IsType<OrderLabelBatch>(Ok(result));
        var document = Assert.Single(batch.Documents);
        Assert.Equal("1000", document.PlatformOrderNumber);
        Assert.Equal(Pdf, document.Content);
        Assert.Empty(batch.Failures);
        Assert.Equal(["aliexpress.logistics.querylogisticsorderdetail", "aliexpress.logistics.redefining.getprintinfos"],
            agent.Calls.Select(call => call.Api));
        Assert.All(agent.Calls, call => Assert.Equal(40, call.Sid));
        Assert.Equal("1000", agent.Calls[0].Parameters["trade_order_id"]);
        Assert.Equal("true", agent.Calls[1].Parameters["print_detail"]);
        using var query = JsonDocument.Parse(agent.Calls[1].Parameters["warehouse_order_query_d_t_os"]);
        var item = Assert.Single(query.RootElement.EnumerateArray());
        Assert.Equal("LP123", item.GetProperty("international_logistics_id").GetString());
        Assert.Equal(777, item.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Works_with_the_real_platform_response_shapes()
    {
        // Given 真实联调（2026-11-26）看到的响应：物流订单多包了几层，面单的 result 是内嵌 JSON 的字符串。
        var embedded = JsonSerializer.Serialize(new { body = Convert.ToBase64String(Pdf) });
        var agent = new FakeAgent(
            logistics: """
                {"aliexpress_logistics_querylogisticsorderdetail_response":{"result":{"success":true,"result_list":
                {"aeop_logistics_order_detail_dto":[{"trade_order_id":1000,"logistics_order_id":777,"international_logistics_num":"LP123"}]}}}}
                """,
            print: JsonSerializer.Serialize(new { aliexpress_logistics_redefining_getprintinfos_response = new { result = embedded } }));
        var printer = new AliExpressOrderLabelPrinter(agent, new TestErpTokenStore("test-token"));

        // When 打印；Then 照样取到 PDF。
        var batch = Ok(await printer.PrintLabelsAsync(Shop, ["1000"], TestContext.Current.CancellationToken));
        Assert.Equal(Pdf, Assert.Single(batch.Documents).Content);
    }

    [Fact]
    public async Task Order_without_online_shipment_fails_alone_without_blocking_other_orders()
    {
        // Given 第一张交易单没有物流订单，第二张有。
        var agent = new FakeAgent(
            logistics: null,
            print: $$$"""{"result":"{{{Convert.ToBase64String(Pdf)}}}"}""",
            logisticsByOrder: new()
            {
                ["1000"] = """{"result":{"success":true,"error_desc":"no data","result_list":[]}}""",
                ["1001"] = """{"result":{"result_list":{"logistics_order_detail":[{"international_logistics_num":"LP9"}]}}}"""
            });
        var printer = new AliExpressOrderLabelPrinter(agent, new TestErpTokenStore("test-token"));

        // When 同时打印；Then 第一张给出失败原因，第二张取得面单（兼容多包一层的响应）。
        var batch = Ok(await printer.PrintLabelsAsync(Shop, ["1000", "1001"], TestContext.Current.CancellationToken));
        Assert.Equal("1001", Assert.Single(batch.Documents).PlatformOrderNumber);
        var failure = Assert.Single(batch.Failures);
        Assert.Equal("1000", failure.PlatformOrderNumber);
        Assert.Contains("no data", failure.Reason);
    }

    [Fact]
    public async Task Non_numeric_order_number_is_reported_without_calling_the_platform()
    {
        var agent = new FakeAgent(null, null);
        var printer = new AliExpressOrderLabelPrinter(agent, new TestErpTokenStore("test-token"));

        var batch = Ok(await printer.PrintLabelsAsync(Shop, ["ABC-1"], TestContext.Current.CancellationToken));

        Assert.Empty(agent.Calls);
        Assert.Equal("ABC-1", Assert.Single(batch.Failures).PlatformOrderNumber);
    }

    [Fact]
    public async Task Missing_erp_token_fails_the_whole_batch()
    {
        var printer = new AliExpressOrderLabelPrinter(new FakeAgent(null, null), new FailingErpTokenStore());

        var result = await printer.PrintLabelsAsync(Shop, ["1000"], TestContext.Current.CancellationToken);

        Assert.True(result.IsErr);
    }

    [Fact]
    public void Finds_base64_pdf_in_plain_wrapped_and_json_embedded_responses()
    {
        var base64 = Convert.ToBase64String(Pdf);
        foreach (var json in new[]
        {
            $$$"""{"result":"{{{base64}}}"}""",
            $$$"""{"x":{"list":[{"result":"{{{base64}}}"}]}}""",
            $$$"""{"result":"{\"success\":true,\"body\":\"{{{base64}}}\"}"}"""
        })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(Pdf, AliExpressOrderLabelPrinter.FindPdf(document.RootElement));
        }
    }

    [Fact]
    public void Ignores_responses_without_a_pdf()
    {
        using var document = JsonDocument.Parse($$$"""{"result":"{{{Convert.ToBase64String("not a pdf"u8.ToArray())}}}","e":"x!"}""");

        Assert.Null(AliExpressOrderLabelPrinter.FindPdf(document.RootElement));
    }

    private static OrderLabelBatch Ok(OpResult<OrderLabelBatch> result)
    {
        Assert.False(result.IsErr);
        return result.Value;
    }

    private sealed record Call(int Sid, string Api, IReadOnlyDictionary<string, string> Parameters);

    private sealed class FakeAgent(string? logistics, string? print, Dictionary<string, string>? logisticsByOrder = null)
        : IPlatformAgentApiClient
    {
        public List<Call> Calls { get; } = [];

        public Task<OpResult<JsonElement>> InvokeAsync(string platform, int sid, string api,
            IReadOnlyDictionary<string, string> parameters, ErpAccessToken accessToken, CancellationToken cancellationToken,
            string? method = null)
        {
            Calls.Add(new(sid, api, parameters));
            var json = !api.EndsWith("getprintinfos", StringComparison.Ordinal)
                ? logisticsByOrder?.GetValueOrDefault(parameters["trade_order_id"]) ?? logistics
                : print;
            using var document = JsonDocument.Parse(json ?? "{}");
            return Task.FromResult(OpResults.Ok(document.RootElement.Clone()));
        }
    }
}
