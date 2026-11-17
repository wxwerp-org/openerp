using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class WxwErpEndpointTests
{
    private const string EmptyShopResponse = """{"success":true,"data_count":0,"page_count":1,"data":{"items":[]}}""";

    [Fact]
    public async Task UsesDefaultEndpointWhenHttpClientHasNoBaseAddress()
    {
        // Given 未配置目标地址，使用真实 WxwErpClient 和确定性 HTTP 替身。
        using var handler = new RecordingHandler(EmptyShopResponse);
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        // When 获取指定页的店铺列表。
        var (result, status) = await client.GetShopsAsync(2, 25, TestContext.Current.CancellationToken);

        // Then 默认主机、接口路径、Token 与请求体保持既有契约。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.Equal(200, status);
        Assert.Equal("https://v3-api.wxwerp.com", client.Configuration.BaseUrl);
        Assert.Equal(new Uri("https://v3-api.wxwerp.com/open-api/shop/get_shops"), handler.RequestUri);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("fake-token", handler.AccessToken);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(2, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("page_size").GetInt32());
        Assert.Equal("TemuPop", body.RootElement.GetProperty("platform").GetString());
    }

    [Fact]
    public async Task UsesHttpClientBaseAddressAndPreservesWxwRequestContract()
    {
        // Given typed HttpClient 指向受控 HTTP 替身。
        using var handler = new RecordingHandler(EmptyShopResponse);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://wxw-api.test:8123/")
        };
        var client = CreateClient(http);

        // When 获取指定页的店铺列表。
        var (result, status) = await client.GetShopsAsync(3, 40, TestContext.Current.CancellationToken);

        // Then 实际目标配置与请求主机更新，接口路径、Token 和请求体不变。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.Equal(200, status);
        Assert.Equal("http://wxw-api.test:8123", client.Configuration.BaseUrl);
        Assert.Equal(new Uri("http://wxw-api.test:8123/open-api/shop/get_shops"), handler.RequestUri);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("fake-token", handler.AccessToken);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(3, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(40, body.RootElement.GetProperty("page_size").GetInt32());
        Assert.Equal("TemuPop", body.RootElement.GetProperty("platform").GetString());
    }

    [Theory]
    [InlineData("ftp://wxw-api.test", "http 或 https")]
    [InlineData("http://user:secret@wxw-api.test", "用户名或密码")]
    public async Task RejectsUnsupportedEndpointBeforeSending(string baseUrl, string expectedMessage)
    {
        // Given ERP 地址使用不支持的协议或包含 URL 用户信息。
        using var handler = new RecordingHandler(EmptyShopResponse);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        // When 请求店铺列表。
        var client = CreateClient(http);
        var (result, status) = await client.GetShopsAsync(1, 100, TestContext.Current.CancellationToken);

        // Then 返回清楚的配置错误，且请求不会发送到目标地址。
        if (!result.IsErr)
            throw new Xunit.Sdk.XunitException("不支持的 ERP BaseUrl 必须返回配置错误。");
        Assert.Equal(503, status);
        Assert.Contains(expectedMessage, result.Error.Message);
        Assert.DoesNotContain("secret", client.Configuration.BaseUrl);
        Assert.Equal(0, handler.SendCount);
    }

    private static WxwErpClient CreateClient(HttpClient http) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp:AccessToken"] = " fake-token "
        }).Build());

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AccessToken { get; private set; }
        public string? Body { get; private set; }
        public int SendCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SendCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            AccessToken = request.Headers.GetValues("AccessToken").Single();
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
