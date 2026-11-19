using System.Net;
using System.Text;
using System.Text.Json;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderReaderTests
{
    private const string FirstId = "000000000000000000000002";
    private const string SecondId = "000000000000000000000001";
    private static readonly ErpAccessToken Token = new("fake-token");
    private const string Count = """{"data_count":2,"page_count":1}""";

    [Fact]
    public async Task ReadsOnlyVisibleShopAndPreservesDetailLinesAndRawTime()
    {
        // Given 目标店铺位于第二页，订单列表缺创建时间，详情含相同 SKU 的两行。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", TwoVisibleShopsFirstPage),
            ("/open-api/shop/get_shops", TwoVisibleShopsSecondPage),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order/get_list", List(FirstId, 30, SecondId, 30)),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", Times),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", Times));
        using var http = new HttpClient(handler);
        var client = new WxwErpClient(http);
        var progress = new List<OrderReadProgress>();

        // When 读取该店全部历史订单。
        var result = await client.ReadOrdersAsync(30, progress.Add, Token, TestContext.Current.CancellationToken);

        // Then 店铺和订单的所有请求使用同一 Token，PO 状态、每行数量和时间来源均保留。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal(2, result.Value.Total);
        Assert.Empty(result.Value.SkipReasons);
        Assert.Equal([FirstId, SecondId], result.Value.Orders.Select(order => order.ErpId));
        foreach (var order in result.Value.Orders)
        {
            Assert.Equal("ERP-1", order.OrderNumber);
            Assert.Equal("Normal", order.Status);
            Assert.Equal(30, order.Sid);
            Assert.Equal(new PlatformOrderSnapshot("PO-1", 30, "4", "已发货"), Assert.Single(order.PlatformOrders));
            Assert.Equal([2, 3], order.Lines.Select(line => line.Quantity));
            Assert.All(order.Lines, line => Assert.Equal("SKU-1", line.SkuId));
            Assert.Equal(new OrderTimeValue("2025-08-12 13:00", "detail"), order.Times.Created);
            Assert.Equal(new OrderTimeValue("2025-08-12 13:01", "list"), order.Times.Paid);
            Assert.Null(order.Times.Dispatched);
        }
        Assert.Equal(2, progress[^1].Processed);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task SkipsWholeOrderWhenAnyChildIsFromAnotherShop()
    {
        // Given 第一张主单的其中一个 PO 来自另一个 SID。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order/get_list", List(FirstId, 9, SecondId, 30)),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", Times));
        using var http = new HttpClient(handler);

        // When 读取历史订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 仅同店订单进入保存集合，异常主单计为跳过且不查询详情。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal(SecondId, Assert.Single(result.Value.Orders).ErpId);
        Assert.Equal(1, Assert.Single(result.Value.SkipReasons).Value);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task RejectsStoreOutsideTheVisibleTemuPopList()
    {
        // Given Token 可见店铺列表只有 SID 30。
        using var handler = new ErpHandler(("/open-api/shop/get_shops", Shops));
        using var http = new HttpClient(handler);

        // When 手动请求同步 SID 9。
        var result = await new WxwErpClient(http).ReadOrdersAsync(9, null, Token, TestContext.Current.CancellationToken);

        // Then 明确拒绝且不调用任何订单接口。
        if (!result.IsErr)
            throw new XunitException("不可见店铺必须以业务失败结束。");
        Assert.Contains("无法访问", result.Error.Message);
        handler.AssertCompleted();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("short")]
    [InlineData("changed")]
    public async Task RejectsIncompletePagination(string problem)
    {
        // Given 统计显示两页，源数据存在重复 ID、提前空页或总数漂移。
        const string twoPages = """{"data_count":2,"page_count":2}""";
        var responses = new List<(string Path, string Data)>
        {
            ("/open-api/order/get_list_page_count", twoPages),
            ("/open-api/order/get_list", List(FirstId, 30)),
            ("/open-api/order/get_list", problem == "short" ? """{"orders":[]}"""
                : List(problem == "duplicate" ? FirstId : SecondId, 30))
        };
        if (problem == "changed")
            responses.Add(("/open-api/order/get_list_page_count", """{"data_count":3,"page_count":3}"""));
        using var handler = new ErpHandler(responses.ToArray());
        using var http = new HttpClient(handler);

        // When 逐页读取。
        var result = await new WxwErpClient(http).ReadOrderIndexAsync(30, 1, null, Token, TestContext.Current.CancellationToken);

        // Then 以分页错误结束，不能把部分 ID 当成成功。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("分页", result.Error.Message);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task ConfirmsOrderAndUsesRefreshedDetailsWhenTimesAreMissing()
    {
        // Given 时间接口返回歧义消息，复核详情确认主单仍可读取，但其中一行数量已更新。
        var currentDetail = Detail.Replace("\"qty\":2", "\"qty\":5", StringComparison.Ordinal);
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", List(FirstId, 30)),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", TimeNotFound),
            ("/open-api/order_detail/get_base", currentDetail));
        using var http = new HttpClient(handler);

        // When 读取订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 创建时间为空，保留列表付款时间，商品使用复核后的详情。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        var order = Assert.Single(result.Value.Orders);
        Assert.Null(order.Times.Created);
        Assert.Equal("list", order.Times.Paid?.Source);
        Assert.Equal([5, 3], order.Lines.Select(line => line.Quantity));
        handler.AssertCompleted();
    }

    [Theory]
    [InlineData("""!{"success":false,"message":"_暂未找到该订单详细信息"}""")]
    [InlineData("""!{"success":false,"message":"AccessToken scope denied"}""")]
    public async Task FailsWholeBatchWhenOrderCannotBeReconfirmedAfterMissingTime(string failedDetail)
    {
        // Given 首单已处理，第二单的时间查不到，复核详情也找不到或无法读取该单。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order/get_list", List(FirstId, 30, SecondId, 30)),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", Times),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", TimeNotFound),
            ("/open-api/order_detail/get_base", failedDetail));
        using var http = new HttpClient(handler);

        // When 读取整店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 批次失败，已经读取的任何主单都不能交给保存步骤。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.NotEmpty(result.Error.Message);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task SkipsOrderWhenReconfirmedDetailNoLongerMatchesShop()
    {
        // Given 补查时间返回歧义消息，复核详情发现主单已不属于该店。
        var movedDetail = Detail.Replace("\"sid\":30", "\"sid\":9", StringComparison.Ordinal);
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", List(FirstId, 30)),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", TimeNotFound),
            ("/open-api/order_detail/get_base", movedDetail));
        using var http = new HttpClient(handler);

        // When 读取该店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 沿用归属异常的跳过规则，旧详情不会进入保存集合。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Empty(result.Value.Orders);
        Assert.Equal(1, Assert.Single(result.Value.SkipReasons).Value);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task RejectsOtherTimeFailuresWithoutRecheckingDetail()
    {
        // Given 时间接口拒绝访问，与缺时间记录的歧义消息不同。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", List(FirstId, 30)),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", """!{"success":false,"message":"AccessToken scope denied"}"""));
        using var http = new HttpClient(handler);

        // When 读取该店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 权限失败直接中止整批。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("访问权限", result.Error.Message);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task FailsBatchWhenDetailRequestFails()
    {
        // Given 列表完整但第二单详情因权限失败。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order/get_list", List(FirstId, 30, SecondId, 30)),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", Times),
            ("/open-api/order_detail/get_base", """!{"success":false,"message":"AccessToken scope denied"}"""));
        using var http = new HttpClient(handler);

        // When 读取整店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 即使首单已读取也不交付部分数据给保存步骤。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("访问权限", result.Error.Message);
        handler.AssertCompleted();
    }

    [Theory]
    [InlineData("""{"name":"_付款","time":123}""")]
    [InlineData("""{"name":"_出库","time":false}""")]
    public async Task RejectsWrongTypesInListTimes(string invalidTime)
    {
        // Given 列表含有效创建时间，但另一项已知时间的原值类型错误。
        var page = List(FirstId, 30).Replace(
            """{"name":"_付款","time":"2025-08-12 13:01"}""",
            """{"name":"_创建","time":"2025-08-12 13:00"},""" + invalidTime, StringComparison.Ordinal);
        using var handler = new ErpHandler(
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", page),
            ("/open-api/order/get_list_page_count", OneOrder));
        using var http = new HttpClient(handler);

        // When 读取订单列表。
        var result = await new WxwErpClient(http).ReadOrderIndexAsync(30, 20, null, Token, TestContext.Current.CancellationToken);

        // Then 整批失败，不能把错误值当作空时间。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("时间", result.Error.Message);
    }

    [Theory]
    [InlineData("skuId", "\"SKU-1\"", "123")]
    [InlineData("skuCode", "\"merchant\"", "{}")]
    [InlineData("opid", "\"P\"", "true")]
    [InlineData("oguid", "\"same\"", "[]")]
    [InlineData("enabled", "false", "\"false\"")]
    [InlineData("append", "false", "0")]
    [InlineData("key", "\"4\"", "4")]
    [InlineData("status", "{\"key\":\"4\",\"text\":\"发货\"}", "[]")]
    public async Task RejectsWrongTypesInDetailFields(string field, string valid, string invalid)
    {
        // Given 首单完整，第二单详情的已知商品字段或 PO 状态字段类型错误。
        var invalidDetail = Detail.Replace($"\"{field}\":{valid}", $"\"{field}\":{invalid}", StringComparison.Ordinal);
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order/get_list", ListWithCreatedTime(FirstId, SecondId)),
            ("/open-api/order/get_list_page_count", Count),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_base", invalidDetail));
        using var http = new HttpClient(handler);

        // When 读取整店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 整批失败，连同首单也不会交给保存步骤。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("格式", result.Error.Message);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task RejectsWrongTypeInPlatformStatusText()
    {
        // Given PO 的归属有效，但列表的 PO 状态文本是数字。
        using var handler = new ErpHandler(
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", List(FirstId, 30).Replace("\"已发货\"", "4", StringComparison.Ordinal)),
            ("/open-api/order/get_list_page_count", OneOrder));
        using var http = new HttpClient(handler);

        // When 读取订单列表。
        var result = await new WxwErpClient(http).ReadOrderIndexAsync(30, 20, null, Token, TestContext.Current.CancellationToken);

        // Then 批次失败，错误状态文本不会被转成空值。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("状态", result.Error.Message);
    }

    [Fact]
    public async Task RejectsWrongTypeInFallbackTime()
    {
        // Given 列表缺创建时间，补查响应把创建时间返回为数字。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", List(FirstId, 30)),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order_detail/get_base", Detail),
            ("/open-api/order_detail/get_time", """{"times":[{"name":"_创建","date":123}]}"""));
        using var http = new HttpClient(handler);

        // When 读取该店订单并补查时间。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 批次失败，不保存缺少创建时间的快照。
        if (!result.IsErr)
            throw new XunitException("该批次必须失败。");
        Assert.Contains("时间", result.Error.Message);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task AcceptsMissingAndNullOptionalFields()
    {
        // Given PO 状态和商品标识有合法缺省及 null，创建时间仍有原值。
        const string detail = """
            {"orderId":"ERP-1","shop":{"sid":30,"platform":"TemuPop"},"status":{"key":"Normal"},
             "platformOrders":[{"oid":"PO-1","status":{"key":null}}],
             "products":[{"skuId":null,"qty":1,"enabled":null}]}
            """;
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", Shops),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order/get_list", ListWithCreatedTime(FirstId).Replace("\"已发货\"", "null", StringComparison.Ordinal)),
            ("/open-api/order/get_list_page_count", OneOrder),
            ("/open-api/order_detail/get_base", detail));
        using var http = new HttpClient(handler);

        // When 读取该店订单。
        var result = await new WxwErpClient(http).ReadOrdersAsync(30, null, Token, TestContext.Current.CancellationToken);

        // Then 原样保留有效字段，合法缺省仍为空。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        var order = Assert.Single(result.Value.Orders);
        Assert.Equal(new PlatformOrderSnapshot("PO-1", 30, null, null), Assert.Single(order.PlatformOrders));
        Assert.Equal(new OrderLineSnapshot(null, null, null, null, 1, null, null), Assert.Single(order.Lines));
        Assert.Equal("2025-08-12 13:00", order.Times.Created?.Raw);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task ReadsEveryVisibleShopWithoutQueryingAuthorizationStatus()
    {
        // Given Token 可见店铺分两页返回，且没有调用授权状态接口。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", TwoVisibleShopsFirstPage),
            ("/open-api/shop/get_shops", TwoVisibleShopsSecondPage));
        using var http = new HttpClient(handler);

        // When 一次性读取全部可见店铺。
        var result = await new WxwErpClient(http).GetAllShopsAsync("TemuPop", Token, TestContext.Current.CancellationToken);

        // Then 合并所有分页，且没有额外的授权状态请求。
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal(["31", "30"], result.Value.Select(shop => shop.Sid));
        Assert.Equal(["店铺三十一", "店铺三十"], result.Value.Select(shop => shop.Name));
        handler.AssertCompleted();
    }

    private const string OneOrder = """{"data_count":1,"page_count":1}""";
    private const string TimeNotFound = """!{"success":false,"message":"_暂未找到信息"}""";
    private const string Shops = """!{"success":true,"data_count":1,"page_count":1,"data":{"items":[{"sid":"30","platform":"TemuPop"}]}}""";
    private const string TwoVisibleShopsFirstPage = """!{"success":true,"data_count":2,"page_count":2,"data":{"items":[{"sid":"31","platform":"TemuPop","name":"店铺三十一"}]}}""";
    private const string TwoVisibleShopsSecondPage = """!{"success":true,"data_count":2,"page_count":2,"data":{"items":[{"sid":"30","platform":"TemuPop","name":"店铺三十"}]}}""";
    private const string Detail = """
        {"orderId":"ERP-1","shop":{"sid":30,"platform":"TemuPop"},"status":{"key":"Normal"},
        "platformOrders":[{"oid":"PO-1","status":{"key":"4","text":"发货"}}],
        "products":[{"opid":"P","oguid":"same","skuId":"SKU-1","skuCode":"merchant","qty":2,"enabled":false,"append":false},
                    {"opid":"P","oguid":"same","skuId":"SKU-1","skuCode":"merchant","qty":3,"enabled":false,"append":false}]}
        """;
    private const string Times = """{"times":[{"name":"_创建","date":"2025-08-12 13:00"},{"name":"_付款","date":"2000-01-01 13:00"},{"name":"_出库","date":"-"}]}""";

    private static string List(string id, int sid, string? secondId = null, int secondSid = 30) =>
        "{\"orders\":[" + Row(id, sid) + (secondId is null ? "" : "," + Row(secondId, secondSid)) + "]}";

    private static string ListWithCreatedTime(string id, string? secondId = null) =>
        List(id, 30, secondId).Replace(
            """{"name":"_付款","time":"2025-08-12 13:01"}""",
            """{"name":"_创建","time":"2025-08-12 13:00"}""", StringComparison.Ordinal);

    private static string Row(string id, int sid) => $$"""
        {"id":"{{id}}","orderid":"ERP-1","sub_orders":[{"id":"PO-1","sid":30,"status":"已发货"}{{(sid == 30 ? "" : ",{\"id\":\"PO-2\",\"sid\":" + sid + "}")}}],
        "times":[{"name":"_付款","time":"2025-08-12 13:01"}]}
        """;

    private sealed class ErpHandler(params (string Path, string Data)[] responses) : HttpMessageHandler
    {
        private int _next;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.True(_next < responses.Length, $"出现额外请求 {request.RequestUri?.AbsolutePath}");
            var (path, data) = responses[_next++];
            Assert.Equal(path, request.RequestUri?.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("fake-token", Assert.Single(request.Headers.GetValues("AccessToken")));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            if (path == "/open-api/shop/get_shops")
                Assert.Equal("TemuPop", body.GetProperty("platform").GetString());
            if (path.StartsWith("/open-api/order/", StringComparison.Ordinal))
            {
                var filters = body.GetProperty("filters");
                Assert.Equal("all", filters.GetProperty("status").GetString());
                Assert.Equal("all", filters.GetProperty("gid").GetString());
                Assert.Equal(40, filters.GetProperty("platforms")[0].GetInt32());
                Assert.Equal("30", filters.GetProperty("shops").GetProperty("values")[0].GetProperty("items")[0].GetProperty("key").GetString());
                if (path.EndsWith("/get_list", StringComparison.Ordinal))
                    Assert.Equal("w_order._id", body.GetProperty("sort").GetProperty("field").GetString());
            }
            if (path.EndsWith("/get_base", StringComparison.Ordinal))
            {
                Assert.False(body.GetProperty("ask").GetBoolean());
                Assert.True(body.GetProperty("showAllProduct").GetBoolean());
            }

            var envelope = data.StartsWith('!') ? data[1..] : "{\"success\":true,\"data\":" + data + "}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            };
        }

        public void AssertCompleted() => Assert.Equal(responses.Length, _next);
    }
}
