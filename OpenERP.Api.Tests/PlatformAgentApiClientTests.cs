using System.Net;
using System.Text;
using System.Text.Json;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class PlatformAgentApiClientTests
{
    private static readonly ErpAccessToken Token = new("erp-token", "https://erp.example.com/");
    private static readonly Dictionary<string, string> Params = new() { ["param_aeop_order_query"] = """{"current_page":1}""" };

    [Fact]
    public async Task Request_goes_to_the_agent_route_with_the_access_token_and_only_the_sid_and_string_params()
    {
        // Given 代理接口返回成功和平台原始响应。
        var handler = new FakeHandler("""{"success":true,"content":{"result":{"total_count":1}}}""");
        var client = new PlatformAgentApiClient(new HttpClient(handler));

        // When 以店铺编号调用速卖通订单列表接口。
        var result = await client.InvokeAsync("aliexpress", 30, "aliexpress.trade.seller.orderlist.get", Params, Token,
            TestContext.Current.CancellationToken);

        // Then 请求发往 /open-api/agent/aliexpress/invoke，带 AccessToken 头；请求体只有 sid、api、params，值都是字符串；成功时返回 content。
        var content = OpResultAssert.Ok(result);
        Assert.Equal(1, content.GetProperty("result").GetProperty("total_count").GetInt32());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://erp.example.com/open-api/agent/aliexpress/invoke", request.Uri);
        Assert.Equal("erp-token", request.AccessToken);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(["api", "params", "sid"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(30, body.RootElement.GetProperty("sid").GetInt32());
        Assert.Equal("aliexpress.trade.seller.orderlist.get", body.RootElement.GetProperty("api").GetString());
        var parameters = body.RootElement.GetProperty("params");
        Assert.All(parameters.EnumerateObject(), property => Assert.Equal(JsonValueKind.String, property.Value.ValueKind));
        Assert.Equal("""{"current_page":1}""", parameters.GetProperty("param_aeop_order_query").GetString());
    }

    [Fact]
    public async Task An_explicit_method_is_sent_in_the_body()
    {
        // Given 调用方指定请求方法。
        var handler = new FakeHandler("""{"success":true,"content":{}}""");

        // When 调用。Then 请求体带 method。
        var result = await new PlatformAgentApiClient(new HttpClient(handler)).InvokeAsync(
            "aliexpress", 30, "some.api", Params, Token, TestContext.Current.CancellationToken, "get");
        OpResultAssert.Ok(result);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("get", body.RootElement.GetProperty("method").GetString());
    }

    [Theory]
    [InlineData("AccessToken invalid", "ERP AccessToken 无效、停用或过期，请在系统设置中检查。")]
    [InlineData("AccessToken expired", "ERP AccessToken 无效、停用或过期，请在系统设置中检查。")]
    [InlineData("AccessToken scope denied", "ERP AccessToken 没有平台接口的访问权限。")]
    [InlineData("Shop not found or not accessible", "旺销王中找不到该店铺，或当前账号无权访问。")]
    [InlineData("Shop platform does not match agent API", "该店铺的平台与所调用的平台接口不一致。")]
    [InlineData("Shop token not found", "该店铺尚未在旺销王完成平台授权。")]
    [InlineData("sid invalid", "店铺编号不正确。")]
    [InlineData("something with secret-value inside", "旺销王平台接口调用失败，请检查 Token 权限、账号额度及店铺授权。")]
    public async Task Failures_are_mapped_to_readable_messages_without_echoing_the_upstream_text(string message, string expected)
    {
        // Given 旺销王返回 success=false 和英文原因。
        var handler = new FakeHandler($$"""{"success":false,"message":"{{message}}"}""");

        // When 调用。Then 返回对应的中文原因，未知原因不透传上游原文。
        var result = await new PlatformAgentApiClient(new HttpClient(handler)).InvokeAsync(
            "aliexpress", 30, "some.api", Params, Token, TestContext.Current.CancellationToken);
        Assert.Equal(expected, OpResultAssert.Err(result));
        Assert.DoesNotContain("secret-value", OpResultAssert.Err(result));
    }

    [Fact]
    public async Task Http_errors_bad_json_and_missing_results_are_reported()
    {
        var ct = TestContext.Current.CancellationToken;

        // Given 上游返回 HTTP 500、无法解析的内容、缺少 success 字段、缺少 content。
        var http500 = await new PlatformAgentApiClient(new HttpClient(new FakeHandler("boom", HttpStatusCode.InternalServerError)))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);
        var badJson = await new PlatformAgentApiClient(new HttpClient(new FakeHandler("not json")))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);
        var noSuccess = await new PlatformAgentApiClient(new HttpClient(new FakeHandler("""{"content":{}}""")))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);
        var noContent = await new PlatformAgentApiClient(new HttpClient(new FakeHandler("""{"success":true}""")))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);

        // Then 每种情况都给出明确的错误。
        Assert.Equal("ERP 平台接口返回 HTTP 500，请检查凭证权限及服务状态。", OpResultAssert.Err(http500));
        Assert.Equal("ERP 平台接口返回了无法解析的响应。", OpResultAssert.Err(badJson));
        Assert.Equal("ERP 平台接口未返回有效的业务结果。", OpResultAssert.Err(noSuccess));
        Assert.Equal("ERP 平台接口未返回平台响应。", OpResultAssert.Err(noContent));
    }

    [Fact]
    public async Task Network_failure_and_timeout_are_reported()
    {
        // Given 网络不可达，以及请求超时。
        var ct = TestContext.Current.CancellationToken;
        var unreachable = await new PlatformAgentApiClient(new HttpClient(new FakeHandler(throws: new HttpRequestException("down"))))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);
        var timeout = await new PlatformAgentApiClient(new HttpClient(new FakeHandler(throws: new TaskCanceledException())))
            .InvokeAsync("aliexpress", 30, "some.api", Params, Token, ct);

        // Then 分别提示无法连接和超时。
        Assert.Equal("无法连接私有 ERP，请检查网络后重试。", OpResultAssert.Err(unreachable));
        Assert.Equal("ERP 平台接口请求超时，请重试。", OpResultAssert.Err(timeout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ali/express")]
    [InlineData("../admin")]
    [InlineData("ali express")]
    public async Task Invalid_platform_names_never_reach_the_network(string platform)
    {
        // Given 平台名称含路径分隔符、点号、空格或为空。
        var handler = new FakeHandler("""{"success":true,"content":{}}""");

        // When 调用。Then 直接拒绝，没有发出请求。
        var result = await new PlatformAgentApiClient(new HttpClient(handler)).InvokeAsync(
            platform, 30, "some.api", Params, Token, TestContext.Current.CancellationToken);
        Assert.Equal("平台名称格式不正确。", OpResultAssert.Err(result));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_missing_or_malformed_erp_token_is_rejected_without_a_request()
    {
        // Given 空 Token，以及含换行的 Token。
        var handler = new FakeHandler("""{"success":true,"content":{}}""");
        var client = new PlatformAgentApiClient(new HttpClient(handler));
        var ct = TestContext.Current.CancellationToken;

        // When 调用。Then 两种都被拒绝，没有发出请求。
        var empty = await client.InvokeAsync("aliexpress", 30, "some.api", Params, new ErpAccessToken(""), ct);
        var newline = await client.InvokeAsync("aliexpress", 30, "some.api", Params, new ErpAccessToken("a\nb"), ct);
        Assert.Equal("尚未配置私有 ERP AccessToken。", OpResultAssert.Err(empty));
        Assert.Equal("ERP AccessToken 配置格式不正确。", OpResultAssert.Err(newline));
        Assert.Empty(handler.Requests);
    }

    private sealed record Captured(HttpMethod Method, string Uri, string? AccessToken, string Body);

    private sealed class FakeHandler(string? body = null, HttpStatusCode status = HttpStatusCode.OK, Exception? throws = null)
        : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!.ToString(),
                request.Headers.TryGetValues("AccessToken", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (throws is not null)
                throw throws;

            return new HttpResponseMessage(status) { Content = new StringContent(body ?? "", Encoding.UTF8, "application/json") };
        }
    }
}
