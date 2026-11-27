using System.Net;
using System.Text;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class PlatformAbilityClientTests
{
    private const string Manifest = """
        {"platform_interfaces":{"items":[
          {"key":"ozon","name":"Ozon","description":"d","officialUrl":"https://docs.ozon.ru/api/seller/",
           "apiPath":"/open-api/agent/ozon/invoke","steps":["a","b"],"requestExample":{"sid":0},
           "apiCategories":[{"id":1,"name":"促销活动","count":2},{"id":2,"name":"支付","count":1,"frozen":true,"frozenReason":"买家侧"}],
           "apis":[
             {"categoryId":1,"category":"促销活动","name":"v1-actions","api":"/v1/actions","method":"GET","description":"x",
              "schemaUrl":"https://v3-api.wxwerp.com/open-api/schema/platform/ozon/v1-actions"},
             {"categoryId":2,"category":"支付","name":"pay","api":"/pay","method":"POST","description":"y","frozen":true,"frozenReason":"买家侧"},
             {"categoryId":1,"category":"促销活动","name":"v1-evil","api":"/e","method":"GET","description":"z",
              "schemaUrl":"/open-api/schema/platform/ozon/../../secret"}]},
          {"key":"walmart","name":"Walmart","apiPath":"/open-api/agent/walmart/invoke"},
          {"key":"bad/key","name":"Bad"}]}}
        """;

    [Fact]
    public async Task Abilities_are_read_from_the_public_manifest_and_platforms_without_an_index_keep_empty_lists()
    {
        // Given 开放 API 清单里有一个带接口索引的平台、一个只有代理入口的平台和一个非法 key。
        var handler = new FakeHandler(Manifest);
        var client = new PlatformAbilityClient(new HttpClient(handler));

        // When 读取能力清单。
        var result = await client.GetAbilitiesAsync("https://erp.example.com/", TestContext.Current.CancellationToken);

        // Then 请求公开清单且不带凭证；非法 key 被丢弃；索引内容被完整映射。
        var abilities = OpResultAssert.Ok(result);
        Assert.Equal("https://erp.example.com/open-api/manifest", Assert.Single(handler.Urls));
        Assert.Equal(["ozon", "walmart"], abilities.Select(ability => ability.Key));

        var ozon = abilities[0];
        Assert.Equal(["a", "b"], ozon.Steps);
        Assert.Equal("""{"sid":0}""", ozon.RequestExample);
        Assert.Equal(2, ozon.Categories.Count);
        Assert.True(ozon.Categories[1].Frozen);
        Assert.Equal("买家侧", ozon.Categories[1].FrozenReason);
        Assert.Equal("v1-actions", ozon.Apis[0].SchemaName);
        Assert.True(ozon.Apis[1].Frozen);
        Assert.Null(ozon.Apis[1].SchemaName);
        Assert.Null(ozon.Apis[2].SchemaName);

        Assert.Empty(abilities[1].Apis);
        Assert.Empty(abilities[1].Categories);
    }

    [Theory]
    [InlineData("oz on", "v1-roles")]
    [InlineData("ozon", "../secret")]
    [InlineData("ozon", "a..b")]
    [InlineData("ozon", "")]
    public async Task Schema_requests_reject_names_that_could_leave_the_schema_directory(string platform, string name)
    {
        var handler = new FakeHandler("{}");

        var result = await new PlatformAbilityClient(new HttpClient(handler)).GetSchemaAsync(
            "https://erp.example.com", platform, name, TestContext.Current.CancellationToken);

        Assert.True(result.IsErr);
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task A_valid_schema_request_goes_to_the_platform_schema_route()
    {
        var handler = new FakeHandler("""{"name":"v1-actions"}""");

        var result = await new PlatformAbilityClient(new HttpClient(handler)).GetSchemaAsync(
            "https://erp.example.com/", "ozon", "v1-actions", TestContext.Current.CancellationToken);

        Assert.Equal("v1-actions", OpResultAssert.Ok(result).GetProperty("name").GetString());
        Assert.Equal("https://erp.example.com/open-api/schema/platform/ozon/v1-actions", Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task An_upstream_error_status_becomes_an_error_result()
    {
        var handler = new FakeHandler("{}", HttpStatusCode.NotFound);

        var result = await new PlatformAbilityClient(new HttpClient(handler)).GetAbilitiesAsync(
            "https://erp.example.com", TestContext.Current.CancellationToken);

        Assert.True(result.IsErr);
    }

    private sealed class FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
