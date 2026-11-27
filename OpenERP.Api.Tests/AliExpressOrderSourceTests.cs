using System.Globalization;
using System.Text.Json;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class AliExpressOrderSourceTests
{
    private static readonly PlatformShopRef Shop = new(30, AliExpressOrderSource.PlatformName, "测试店铺");

    [Fact]
    public async Task Orders_are_read_through_the_agent_with_only_the_sid_and_mapped_across_pages()
    {
        // Given 51 张订单分两页返回（总页数 2），订单列表里已带子订单。
        var agent = new FakeAgent(total: 51, pageSize: 50);
        var source = Create(agent);
        var progress = new List<OrderReadProgress>();

        // When 读取店铺订单。
        var result = await source.ReadOrdersAsync(Shop, progress.Add, TestContext.Current.CancellationToken);

        // Then 两页都被读取，只调用订单列表接口，映射为统一订单快照。
        var batch = Unwrap(result);
        Assert.Equal(51, batch.Total);
        Assert.Equal(51, batch.Orders.Count);
        Assert.Empty(batch.SkipReasons);
        Assert.Equal([1, 2], agent.Pages);
        Assert.All(agent.Calls, call =>
        {
            Assert.Equal("aliexpress", call.Platform);
            Assert.Equal(30, call.Sid);
            Assert.Equal("aliexpress.trade.seller.orderlist.get", call.Api);
            Assert.Null(call.Method);
            Assert.Equal("test-token", call.Token);
            Assert.Equal(["param_aeop_order_query"], call.Parameters.Keys);
        });
        var first = Assert.Single(batch.Orders, order => order.OrderNumber == "1000");
        Assert.Equal("AliExpress:30:1000", first.ErpId);
        Assert.Equal(30, first.Sid);
        Assert.Equal("AliExpress", first.Platform);
        Assert.Equal("WAIT_SELLER_SEND_GOODS", first.Status);
        Assert.Equal(new OrderTimeValue("2026-11-01 10:00:00", "AliExpress"), first.Times.Created);
        Assert.Equal(new OrderTimeValue("2026-11-02 11:00:00", "AliExpress"), first.Times.Paid);
        Assert.Equal(new OrderTimeValue("2026-11-03 12:00:00", "AliExpress"), first.Times.Dispatched);
        var reference = Assert.Single(first.PlatformOrders);
        Assert.Equal("1000", reference.PoNumber);
        Assert.Equal("WAIT_SELLER_SEND_GOODS", reference.StatusCode);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(new OrderLineSnapshot("5001", "9001", null, "SKU-A", 2, null, null), first.Lines[0]);
        Assert.Equal(3, first.Lines[1].Quantity);
        Assert.Equal(5, first.Lines.Sum(line => line.Quantity));
        Assert.Equal("pages", progress[0].Stage);
        Assert.Equal("details", progress[^1].Stage);
        Assert.Equal(51, progress[^1].Processed);
    }

    [Fact]
    public async Task The_query_is_a_json_string_with_a_pacific_time_window_and_all_parameter_values_are_strings()
    {
        // Given 固定的当前时间：2026-11-25 20:00 UTC，对应美国太平洋时间 12:00（PST，UTC-8）。
        var agent = new FakeAgent(total: 1, pageSize: 50);
        var source = Create(agent, new FixedTime(new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero)));

        // When 读取订单。
        Unwrap(await source.ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));

        // Then 参数值全是字符串，订单查询是 JSON 文本，创建时间区间按太平洋时间格式化并覆盖 180 天。
        var call = Assert.Single(agent.Calls);
        var text = Assert.Single(call.Parameters.Values);
        using var query = JsonDocument.Parse(text);
        Assert.Equal(1, query.RootElement.GetProperty("current_page").GetInt32());
        Assert.Equal(50, query.RootElement.GetProperty("page_size").GetInt32());
        Assert.Equal("2026-11-25 12:00:00", query.RootElement.GetProperty("create_date_end").GetString());
        var start = DateTime.ParseExact(query.RootElement.GetProperty("create_date_start").GetString()!,
            "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Equal(new DateTime(2026, 11, 25, 12, 0, 0).AddDays(-180), start);
    }

    [Fact]
    public async Task A_result_wrapped_in_the_platform_response_envelope_is_read_the_same_way()
    {
        // Given 旺销王返回的平台原始响应带着外层的 aliexpress_..._response。
        var agent = new FakeAgent(total: 3, pageSize: 50) { Wrap = true };
        var source = Create(agent);

        // When 读取订单。Then 与展开的响应读取结果一致。
        var batch = Unwrap(await source.ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        Assert.Equal(3, batch.Orders.Count);
        Assert.Equal([1], agent.Pages);
    }

    [Fact]
    public async Task Paging_stops_on_a_short_page_when_the_platform_gives_no_total_pages()
    {
        // Given 平台没有返回 total_page，第一页只有 3 条。
        var agent = new FakeAgent(total: 3, pageSize: 50) { OmitTotalPage = true };

        // When 读取订单。Then 只请求一页。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        Assert.Equal(3, batch.Orders.Count);
        Assert.Equal([1], agent.Pages);
    }

    [Fact]
    public async Task Lists_wrapped_in_a_single_array_object_are_expanded()
    {
        // Given 平台把 product_list 再包一层（{"order_product_dto":[...]}），数量以字符串返回。
        var agent = new FakeAgent(total: 1, pageSize: 50)
        {
            Rows =
            [
                """{"order_id":7,"order_status":"WAIT_SELLER_SEND_GOODS","product_list":{"order_product_dto":[{"product_id":1,"child_id":71,"sku_code":"A","product_count":"2"},{"product_id":2,"child_id":72,"product_count":1}]}}"""
            ]
        };

        // When 读取订单。Then 展开后得到两行商品，订单没有被跳过。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        var order = Assert.Single(batch.Orders);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(3, order.Lines.Sum(line => line.Quantity));
        Assert.Empty(batch.SkipReasons);
    }

    [Fact]
    public async Task The_real_platform_response_shape_is_read_without_skipping_orders()
    {
        // Given 旺销王代理返回的真实结构：target_list 和 product_list 都再包了一层（字段名取自真实响应，内容为示例）。
        var agent = new FakeAgent(total: 1, pageSize: 50)
        {
            Content = """
                {"aliexpress_trade_seller_orderlist_get_response":{"result":{
                  "error_message":"The operation is successful.","success":true,"total_count":1,"total_page":1,
                  "current_page":1,"page_size":50,"error_code":"0",
                  "target_list":{"aeop_order_item_dto":[{
                    "order_id":3078195797082516,"order_status":"WAIT_SELLER_SEND_GOODS",
                    "gmt_create":"2026-11-25 01:26:10","gmt_pay_time":"2026-11-25 01:26:36",
                    "product_list":{"aeop_order_product_dto":[{
                      "child_id":3078195797092516,"order_id":3078195797092516,"product_id":1005007412124487,
                      "product_count":2,"sku_code":"91c9fb-1-1-Gift Box Set","son_order_status":"WAIT_SELLER_SEND_GOODS"}]}}]}},
                  "request_id":"x"}}
                """
        };

        // When 读取订单。Then 订单、商品行和数量都被读到，没有被跳过。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        var order = Assert.Single(batch.Orders);
        Assert.Equal("AliExpress:30:3078195797082516", order.ErpId);
        var line = Assert.Single(order.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal("91c9fb-1-1-Gift Box Set", line.SkuCode);
        Assert.Empty(batch.SkipReasons);
    }

    [Fact]
    public async Task Product_name_image_specs_and_unit_price_are_read_from_each_product()
    {
        // Given 商品行带有名称、图片、规格（product_standard）和 {amount, currency_code} 形式的单价。
        var agent = new FakeAgent(total: 1, pageSize: 50)
        {
            Content = """
                {"result":{"success":true,"total_count":1,"total_page":1,"target_list":[{
                  "order_id":8,"order_status":"X","product_list":[{
                    "product_id":1,"product_count":2,"sku_code":"S1","product_name":"Bluetooth Earbuds",
                    "product_img_url":"https://img.example/a.jpg","product_standard":"Color: Black; Size: M",
                    "product_unit_price":{"amount":"9.90","currency_code":"USD"}},
                    {"product_id":2,"product_count":1,"product_unit_price":"3.5"}]}]}}
                """
        };

        // When 读取订单。Then 商品信息被保留；缺失的字段为空。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        var lines = Assert.Single(batch.Orders).Lines;
        Assert.Equal("Bluetooth Earbuds", lines[0].Name);
        Assert.Equal("https://img.example/a.jpg", lines[0].ImageUrl);
        Assert.Equal("Color: Black; Size: M", lines[0].Specs);
        Assert.Equal(9.90m, lines[0].UnitPrice);
        Assert.Equal("USD", lines[0].Currency);
        Assert.Null(lines[1].Name);
        Assert.Equal(3.5m, lines[1].UnitPrice);
        Assert.Null(lines[1].Currency);
    }

    [Fact]
    public async Task Order_amount_buyer_logistics_and_other_fields_are_kept_as_platform_neutral_details()
    {
        // Given 订单带买家、支付方式、金额对象，商品带物流服务和子订单状态。
        var agent = new FakeAgent(total: 1, pageSize: 50)
        {
            Content = """
                {"result":{"success":true,"total_count":1,"total_page":1,"target_list":[{
                  "order_id":9,"order_status":"X","buyer_login_id":"b01","payment_type":"Credit Card","extra_flag":"Y",
                  "pay_amount":{"amount":"19.80","currency_code":"USD"},
                  "product_list":[{"product_id":1,"product_count":2,"logistics_service_name":"Cainiao","son_order_status":"WAIT"}]}]}}
                """
        };

        // When 读取订单。Then 金额、买家、物流成为一等字段，其余按名称 + 取值输出。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        var order = Assert.Single(batch.Orders);
        Assert.Equal("b01", order.Buyer);
        Assert.Equal(19.80m, order.TotalAmount);
        Assert.Equal("USD", order.TotalCurrency);
        Assert.Equal("Cainiao", order.Logistics);
        Assert.Contains("支付方式", order.Details);
        Assert.Contains("extra_flag", order.Details);
        Assert.DoesNotContain("pay_amount", order.Details);
        Assert.Contains("子订单状态", order.Lines[0].Details);
    }

    [Fact]
    public async Task Embedded_json_is_flattened_into_the_more_group_and_logistics_amount_is_a_price_detail()
    {
        // Given 商品行带内嵌 JSON 的 extend_map、物流费用，以及与单价重复的商品小计。
        var agent = new FakeAgent(total: 1, pageSize: 50)
        {
            Content = """
                {"result":{"success":true,"total_count":1,"total_page":1,"target_list":[{
                  "order_id":10,"order_status":"X","product_list":[{
                    "product_id":1,"product_count":1,"logistics_amount":"CNY 1.02","total_product_amount":"CNY 65.34",
                    "send_goods_operator":"SELLER_SEND_GOODS",
                    "extend_map":"{\"length\":\"10\",\"rs\":\"{\\\"timeoutType\\\":96}\"}"}]}]}}
                """
        };

        // When 读取订单。Then 内嵌 JSON 被展开为 more 分组下的路径条目，小计被去掉，物流费用归 price。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));
        var details = System.Text.Json.JsonSerializer.Deserialize<OrderDetail[]>(Assert.Single(batch.Orders).Lines[0].Details!)!;
        Assert.Contains(details, d => d is { Label: "物流费用", Group: "price" });
        Assert.Contains(details, d => d is { Label: "产品状态", Group: "status" });
        Assert.Contains(details, d => d is { Label: "扩展信息.length", Value: "10", Group: "more" });
        Assert.Contains(details, d => d is { Label: "扩展信息.rs.timeoutType", Value: "96", Group: "more" });
        Assert.DoesNotContain(details, d => d.Label == "商品小计");
        Assert.DoesNotContain(details, d => d.Value.StartsWith('{'));
    }

    [Fact]
    public async Task Invalid_orders_are_skipped_with_reasons_and_the_rest_are_kept()
    {
        // Given 一页里有缺少订单号、缺少商品行、商品数量非法的订单和一张正常订单。
        var agent = new FakeAgent(total: 4, pageSize: 50)
        {
            Rows =
            [
                """{"gmt_create":"2026-11-01 10:00:00","order_status":"X","product_list":[{"product_count":1}]}""",
                """{"order_id":2,"order_status":"X"}""",
                """{"order_id":3,"order_status":"X","product_list":[{"product_id":1,"product_count":"abc"}]}""",
                """{"order_id":4,"order_status":"FINISH","product_list":[{"product_id":1,"product_count":"2","sku_code":"S"}]}"""
            ]
        };

        // When 读取订单。
        var batch = Unwrap(await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken));

        // Then 只有正常订单被保留（字符串数量也能解析），其余按原因计入跳过。
        var order = Assert.Single(batch.Orders);
        Assert.Equal("4", order.OrderNumber);
        Assert.Equal(2, order.Lines[0].Quantity);
        Assert.Equal(4, batch.Total);
        Assert.Equal(1, batch.SkipReasons["列表缺少订单号"]);
        Assert.Equal(1, batch.SkipReasons["列表缺少商品行"]);
        Assert.Equal(1, batch.SkipReasons["商品数量格式不正确"]);
    }

    [Theory]
    [InlineData("""{"error_response":{"code":"InvalidAppKey","msg":"The specified App Key is invalid"}}""",
        "AliExpress 返回错误 InvalidAppKey：The specified App Key is invalid")]
    [InlineData("""{"result":{"success":false,"error_code":"isp.50001","error_message":"query order fail"}}""",
        "AliExpress 订单列表查询失败：query order fail")]
    [InlineData("""{"result":{"success":false,"error_code":"40106"}}""", "AliExpress 订单列表查询失败：40106")]
    [InlineData("""{"unexpected":true}""", "AliExpress 订单列表响应格式不正确。")]
    public async Task Platform_level_errors_abort_the_read(string content, string expected)
    {
        // Given 平台对订单列表请求返回错误或无法识别的内容。
        var agent = new FakeAgent(total: 1, pageSize: 50) { Content = content };

        // When 读取订单。Then 整个读取失败并给出可读原因。
        var result = await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken);
        Assert.Equal(expected, OpResultAssert.Err(result));
    }

    [Fact]
    public async Task Agent_errors_are_propagated_unchanged()
    {
        // Given 旺销王代理接口返回错误（如店铺尚未授权）。
        var agent = new FakeAgent(total: 1, pageSize: 50) { Failure = "该店铺尚未在旺销王完成平台授权。" };

        // When 读取订单。Then 错误原样返回，只调用了一次。
        var result = await Create(agent).ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken);
        Assert.Equal("该店铺尚未在旺销王完成平台授权。", OpResultAssert.Err(result));
        Assert.Single(agent.Calls);
    }

    [Fact]
    public async Task Reading_fails_before_calling_the_agent_when_the_erp_token_is_not_configured()
    {
        // Given ERP Token 尚未配置。
        var agent = new FakeAgent(total: 1, pageSize: 50);
        var source = new AliExpressOrderSource(agent, new TestErpTokenStore(""), TimeProvider.System);

        // When 读取订单。Then 失败，没有调用代理接口。
        var result = await source.ReadOrdersAsync(Shop, null, TestContext.Current.CancellationToken);
        Assert.Equal("尚未配置私有 ERP AccessToken。", OpResultAssert.Err(result));
        Assert.Empty(agent.Calls);
    }

    private static AliExpressOrderSource Create(FakeAgent agent, TimeProvider? time = null) =>
        new(agent, new TestErpTokenStore("test-token"), time ?? TimeProvider.System);

    private static OrderImportBatch Unwrap(OpResult<OrderImportBatch> result)
    {
        if (result.IsErr)
            throw new XunitException(OpResultAssert.Err(result));
        return result.Value;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed record AgentCall(
        string Platform, int Sid, string Api, string? Method, string Token, IReadOnlyDictionary<string, string> Parameters);

    /// <summary>可控的代理接口：按页返回订单列表，可改成错误、自定义内容或自定义订单行。</summary>
    private sealed class FakeAgent(int total, int pageSize) : IPlatformAgentApiClient
    {
        public List<AgentCall> Calls { get; } = [];
        public List<int> Pages => Calls.Select(call => JsonDocument.Parse(call.Parameters["param_aeop_order_query"])
            .RootElement.GetProperty("current_page").GetInt32()).ToList();
        public bool Wrap { get; init; }
        public bool OmitTotalPage { get; init; }
        public string? Content { get; init; }
        public string? Failure { get; init; }
        public string[]? Rows { get; init; }

        public Task<OpResult<JsonElement>> InvokeAsync(string platform, int sid, string api,
            IReadOnlyDictionary<string, string> parameters, ErpAccessToken accessToken, CancellationToken cancellationToken,
            string? method = null)
        {
            Calls.Add(new(platform, sid, api, method, accessToken.Value, parameters));
            if (Failure is not null)
                return Task.FromResult<OpResult<JsonElement>>(OpResults.Err(Failure));

            var json = Content ?? Page(Calls.Count);
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(OpResults.Ok(document.RootElement.Clone()));
        }

        private string Page(int page)
        {
            var rows = Rows ?? Enumerable.Range((page - 1) * pageSize, Math.Max(0, Math.Min(pageSize, total - (page - 1) * pageSize)))
                .Select(index => $$"""
                    {"order_id":{{1000 + index}},"order_status":"WAIT_SELLER_SEND_GOODS","gmt_create":"2026-11-01 10:00:00",
                     "gmt_pay_time":"2026-11-02 11:00:00","gmt_send_goods_time":"2026-11-03 12:00:00",
                     "product_list":[{"product_id":5001,"child_id":9001,"sku_code":"SKU-A","product_count":2},
                                     {"product_id":5002,"child_id":9002,"sku_code":"SKU-B","product_count":3}]}
                    """);
            var pages = OmitTotalPage ? "" : $",\"total_page\":{(total + pageSize - 1) / pageSize}";
            var result = $$"""{"success":true,"current_page":{{page}},"page_size":{{pageSize}},"total_count":{{total}}{{pages}},"target_list":[{{string.Join(",", rows)}}]}""";
            return Wrap
                ? $$"""{"aliexpress_trade_seller_orderlist_get_response":{"result":{{result}} } }"""
                : $$"""{"result":{{result}}}""";
        }
    }
}
