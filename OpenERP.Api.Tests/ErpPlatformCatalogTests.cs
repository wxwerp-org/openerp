using System.Net;
using System.Text;
using System.Text.Json;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ErpPlatformCatalogTests
{
    [Fact]
    public async Task Reads_platform_catalog_from_shop_groups_and_platform_enum()
    {
        // Given 上游店铺分组给出有店铺的平台，平台枚举给出展示名、图标和生产标记。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", """
                !{"success":true,"data_count":7,"page_count":1,"data":{"items":[]},
                  "platforms":[
                    {"platform":"TemuPop","text":"_Temu半托管","count":"4","regions":[]},
                    {"platform":"AliExpress","text":"_速卖通","count":"3","regions":[]},
                    {"platform":"Empty","text":"_空平台","count":"0","regions":[]}
                  ]}
                """),
            ("/open-api/system/get_platforms", """
                !{"success":true,"items":[
                    {"name":"TemuPop","value":40,"text":"_Temu半托管","icon":"icon-temupop","isProduction":true},
                    {"name":"AliExpress","value":3,"text":"_速卖通","icon":"icon-aliexpress","isProduction":true}
                  ]}
                """));
        using var http = new HttpClient(handler);

        // When 读取平台目录。
        var (result, status) = await new WxwErpClient(http).GetPlatformsAsync(Token, TestContext.Current.CancellationToken);

        // Then 只保留有店铺的平台，展示名去掉上游翻译前缀，并按店铺数排序。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.Equal(200, status);
        Assert.Equal(["TemuPop", "AliExpress"], result.Value.Select(platform => platform.Name));
        Assert.Equal(["Temu半托管", "速卖通"], result.Value.Select(platform => platform.DisplayName));
        Assert.Equal([4, 3], result.Value.Select(platform => platform.ShopCount));
        Assert.Equal("icon-temupop", result.Value[0].Icon);
        Assert.All(result.Value, platform => Assert.True(platform.IsProduction));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Platform_catalog_keeps_shop_group_display_name_when_enum_read_fails()
    {
        // Given 店铺分组可读，但平台枚举被上游拒绝。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", """
                !{"success":true,"data_count":3,"page_count":1,"data":{"items":[]},
                  "platforms":[{"platform":"AliExpress","text":"_速卖通","count":"3","regions":[]}]}
                """),
            ("/open-api/system/get_platforms", """
                !{"success":false,"message":"AccessToken scope denied"}
                """));
        using var http = new HttpClient(handler);

        // When 读取平台目录。
        var (result, _) = await new WxwErpClient(http).GetPlatformsAsync(Token, TestContext.Current.CancellationToken);

        // Then 目录仍可用，仅缺少枚举补充信息。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        var platform = Assert.Single(result.Value);
        Assert.Equal("AliExpress", platform.Name);
        Assert.Equal("速卖通", platform.DisplayName);
        Assert.Null(platform.Icon);
        Assert.True(platform.IsProduction);
        Assert.Equal(3, platform.ShopCount);
    }

    [Fact]
    public async Task Shop_list_accepts_platforms_other_than_temupop()
    {
        // Given 上游第一页返回一家 AliExpress 店铺。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", """
                !{"success":true,"data_count":1,"page_count":1,"data":{"items":[
                    {"sid":"6","platform":"AliExpress","name":"ae 店铺","region":"US","memberid":"member-6"}
                  ]}}
                """));
        using var http = new HttpClient(handler);

        // When 不按平台筛选读取店铺。
        var (result, status) = await new WxwErpClient(http).GetShopsAsync(
            1, 20, null, Token, TestContext.Current.CancellationToken);

        // Then 不再拒绝非 TemuPop 店铺，并保留上游提供的成员 ID。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.Equal(200, status);
        var shop = Assert.Single(result.Value.Items);
        Assert.Equal("6", shop.Sid);
        Assert.Equal("AliExpress", shop.Platform);
        Assert.Equal("member-6", shop.MemberId);
        Assert.Equal(1, handler.RequestCount);
        Assert.False(handler.Bodies[0].TryGetProperty("platform", out _));
    }

    [Fact]
    public async Task Shop_list_passes_the_platform_filter_to_erp()
    {
        // Given 上游支持按平台枚举名称筛选店铺。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_shops", """
                !{"success":true,"data_count":1,"page_count":1,"data":{"items":[
                    {"sid":"6","platform":"AliExpress","name":"ae 店铺"}
                  ]}}
                """));
        using var http = new HttpClient(handler);

        // When 按 AliExpress 筛选。
        var (result, _) = await new WxwErpClient(http).GetShopsAsync(
            1, 20, "AliExpress", Token, TestContext.Current.CancellationToken);

        // Then 平台条件原样传给上游。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.Equal("AliExpress", handler.Bodies[0].GetProperty("platform").GetString());
    }

    [Fact]
    public async Task Reads_shop_status_fields_from_erp()
    {
        // Given 上游返回单店授权状态。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_status",
                """!{"success":true,"data":{"isTokenOk":true,"region":"US","isSupportUpdateNumber":true}}"""));
        using var http = new HttpClient(handler);

        // When 刷新该店授权状态。
        var result = await new WxwErpClient(http).GetShopStatusAsync("30", Token, TestContext.Current.CancellationToken);

        // Then 只映射上游实际返回的字段。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.True(result.Value.IsTokenOk);
        Assert.Equal("US", result.Value.Region);
        Assert.True(result.Value.IsSupportUpdateNumber);
        Assert.Equal("30", handler.Bodies[0].GetProperty("sid").GetString());
    }

    [Fact]
    public async Task Shop_status_uses_passed_token_without_reading_token_store()
    {
        // Given 状态查询只需要调用方传入的 Token，不应再访问 scoped DbContext。
        using var handler = new ErpHandler(
            ("/open-api/shop/get_status", """!{"success":true,"data":{"isTokenOk":true,"region":"US"}}"""))
        {
            ExpectedToken = "explicit-token"
        };
        using var http = new HttpClient(handler);
        var client = new WxwErpClient(http);

        // When 使用显式 Token 查询单店状态。
        var result = await client.GetShopStatusAsync("30", new ErpAccessToken("explicit-token"), TestContext.Current.CancellationToken);

        // Then 请求成功，且没有读取 TokenStore。
        if (result.IsErr)
            throw new Xunit.Sdk.XunitException(result.Error.Message);
        Assert.True(result.Value.IsTokenOk);
        Assert.Equal(1, handler.RequestCount);
    }

    private static readonly ErpAccessToken Token = new("fake-token");

    private sealed class ErpHandler(params (string Path, string Data)[] responses) : HttpMessageHandler
    {
        private int _next;

        public int RequestCount => _next;

        public string ExpectedToken { get; init; } = "fake-token";

        public List<JsonElement> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.True(_next < responses.Length, $"出现额外请求 {request.RequestUri?.AbsolutePath}");
            var (path, data) = responses[_next++];
            Assert.Equal(path, request.RequestUri?.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(ExpectedToken, Assert.Single(request.Headers.GetValues("AccessToken")));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Bodies.Add(json.RootElement.Clone());

            var envelope = data.StartsWith('!') ? data[1..] : "{\"success\":true,\"data\":" + data + "}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            };
        }
    }
}
