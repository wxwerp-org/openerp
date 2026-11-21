using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenERP.Api.Authentication;
using OpResult;
using OpenERP.Api.Reports;
using OpenERP.Core.Reports;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSnapshotReportEndpointTests
{
    [Fact]
    public async Task Reads_filters_and_returns_utc_query_time_without_caching()
    {
        // Given 可替换的报表实现，不依赖 ERP 或 EF。
        await using var host = await StartAsync();

        // When 选择两个店铺及平台并翻页。
        using var response = await host.Http.GetAsync(
            "/api/reports/order-snapshot?platform=TemuPop&shops=a&shops=b&page=2&pageSize=10", TestContext.Current.CancellationToken);

        // Then 过滤器完整传递，返回 UTC 时间和业务结果，禁止缓存报表快照。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.IsType<OrderSnapshotReportRequest>(host.Query.Request);
        Assert.Equal("TemuPop", request.Platform);
        Assert.Equal(["a", "b"], request.Shops);
        Assert.Equal(2, request.Page);
        Assert.Equal(10, request.PageSize);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("2026-11-17T01:02:03Z", json.GetProperty("generatedAtUtc").GetString());
        Assert.Equal(7, json.GetProperty("summary").GetProperty("itemQuantity").GetInt64());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Rejects_invalid_queries_before_calling_the_provider(string query)
    {
        // Given 接口可用；When 提交无效分页；Then 返回400且不查询存储。
        await using var host = await StartAsync();
        using var response = await host.Http.GetAsync($"/api/reports/order-snapshot?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(host.Query.Request);
    }

    [Fact]
    public async Task Provider_failure_is_not_an_empty_successful_report()
    {
        // Given 数据读取暂不可用；When 查询报表；Then 返回503和可读原因，不返回全零。
        await using var host = await StartAsync();
        host.Query.Fail = true;
        using var response = await host.Http.GetAsync("/api/reports/order-snapshot", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("读取本地订单失败，请重试。", json.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("", 7, null)]
    [InlineData("?trendDays=7", 7, null)]
    [InlineData("?trendDays=30&trendEnd=2026-11-17", 30, "2026-11-17")]
    public async Task Passes_trend_parameters_and_writes_the_report_contract(string query, int days, string? end)
    {
        // Given 可控的报表结果和应用注册的真实路由委托。
        // When 请求默认或显式趋势窗口。
        var (response, json, provider) = await InvokeRouteAsync(query);

        // Then 参数、缓存头和所有趋势与金额字段符合 JSON 契约。
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal(days, provider.Request!.TrendDays);
        Assert.Equal(end, provider.Request.TrendEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var sales = json.GetProperty("sales");
        Assert.Equal(12.34m, sales.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", sales.GetProperty("currency").GetString());
        Assert.Equal(1, sales.GetProperty("knownOrderCount").GetInt64());
        Assert.Equal(1, sales.GetProperty("unknownOrderCount").GetInt64());
        var trend = json.GetProperty("trend");
        Assert.Equal(days, trend.GetProperty("days").GetInt32());
        Assert.Equal("2026-11-17", trend.GetProperty("end").GetString());
        Assert.Equal("USD", trend.GetProperty("currency").GetString());
        Assert.Equal(2, trend.GetProperty("missingCreatedCount").GetInt64());
        Assert.Equal(1, trend.GetProperty("amountKnownOrderCount").GetInt64());
        Assert.Equal(0, trend.GetProperty("amountUnknownOrderCount").GetInt64());
        var points = trend.GetProperty("points");
        Assert.Equal(days, points.GetArrayLength());
        var point = points[days - 1];
        Assert.Equal("2026-11-17", point.GetProperty("date").GetString());
        Assert.Equal(1, point.GetProperty("orderCount").GetInt64());
        Assert.Equal(7, point.GetProperty("itemQuantity").GetInt64());
        Assert.Equal(12.34m, point.GetProperty("salesAmount").GetDecimal());
        var previous = trend.GetProperty("previous");
        Assert.Equal(1, previous.GetProperty("orderCount").GetInt64());
        Assert.Equal(3, previous.GetProperty("itemQuantity").GetInt64());
        Assert.Equal(JsonValueKind.Null, previous.GetProperty("salesAmount").ValueKind);
    }

    [Theory]
    [InlineData("?trendDays=0", "趋势周期只支持 7 或 30 天。")]
    [InlineData("?trendDays=14", "趋势周期只支持 7 或 30 天。")]
    [InlineData("?trendDays=31", "趋势周期只支持 7 或 30 天。")]
    [InlineData("?trendDays=abc", "趋势周期只支持 7 或 30 天。")]
    [InlineData("?trendEnd=", "趋势结束日期必须使用 yyyy-MM-dd 格式。")]
    [InlineData("?trendEnd=2026-02-30", "趋势结束日期必须使用 yyyy-MM-dd 格式。")]
    [InlineData("?trendEnd=2026-1-07", "趋势结束日期必须使用 yyyy-MM-dd 格式。")]
    [InlineData("?trendEnd=2026-11-17T00:00:00Z", "趋势结束日期必须使用 yyyy-MM-dd 格式。")]
    public async Task Rejects_invalid_trend_parameters_with_specific_problem_details(string query, string title)
    {
        // Given 可用的报表路由；When 传入非法趋势参数；Then 禁止缓存并返回 400 及对应文案。
        var (response, json, provider) = await InvokeRouteAsync(query);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal(title, json.GetProperty("title").GetString());
        Assert.Null(provider.Request);
    }

    private static async Task<(HttpResponse Response, JsonElement Json, TestQuery Provider)> InvokeRouteAsync(string query)
    {
        var builder = WebApplication.CreateEmptyBuilder(new());
        builder.WebHost.UseKestrelCore();
        var provider = new TestQuery();
        builder.Services.AddSingleton<IOrderSnapshotReportQuery>(provider);
        await using var app = builder.Build();
        app.MapOrderReports();
        var endpoint = Assert.Single(Assert.Single(((IEndpointRouteBuilder)app).DataSources).Endpoints);
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/reports/order-snapshot";
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = body;
        await endpoint.RequestDelegate!(context);
        body.Position = 0;
        return (context.Response, await JsonSerializer.DeserializeAsync<JsonElement>(body,
            cancellationToken: TestContext.Current.CancellationToken), provider);
    }

    private static async Task<TestHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var query = new TestQuery();
        builder.Services.AddSingleton<IOrderSnapshotReportQuery>(query);
        builder.Services.AddDbContext<AppDbContext>();
        ErpEndpointTestAuth.Configure(builder);
        var app = builder.Build();
        app.MapOrderReports();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new(app, ErpEndpointTestAuth.CreateClient(app, UserAccount.AdministratorRole), query);
    }

    private sealed class TestQuery : IOrderSnapshotReportQuery
    {
        public OrderSnapshotReportRequest? Request { get; private set; }
        public bool Fail { get; set; }

        public Task<OpResult<OrderSnapshotReport>> QueryAsync(OrderSnapshotReportRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            var end = new DateOnly(2026, 11, 17);
            var points = Enumerable.Range(1 - request.TrendDays, request.TrendDays)
                .Select(offset => new OrderTrendPoint(end.AddDays(offset).ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture), offset == 0 ? 1 : 0,
                    offset == 0 ? 7 : 0, offset == 0 ? 12.34m : 0m)).ToArray();
            OpResult<OrderSnapshotReport> result = Fail
                ? OpResults.Err("读取本地订单失败，请重试。")
                : new OrderSnapshotReport(new(2026, 11, 17, 1, 2, 3, DateTimeKind.Utc), new(2, 7), [], [], [], [], 2, 10, 2,
                    new(12.34m, "USD", 1, 1), new(request.TrendDays, end, "USD", points, new(1, 3, null), 2, 1, 0));
            return Task.FromResult(result);
        }
    }

    private sealed class TestHost(WebApplication app, HttpClient http, TestQuery query) : IAsyncDisposable
    {
        public HttpClient Http => http;
        public TestQuery Query => query;
        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            await app.DisposeAsync();
        }
    }
}
