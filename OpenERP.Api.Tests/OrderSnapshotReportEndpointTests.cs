using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
            OpResult<OrderSnapshotReport> result = Fail
                ? OpResults.Err("读取本地订单失败，请重试。")
                : new OrderSnapshotReport(new(2026, 11, 17, 1, 2, 3, DateTimeKind.Utc), new(2, 7), [], [], [], [], 2, 10, 2);
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
