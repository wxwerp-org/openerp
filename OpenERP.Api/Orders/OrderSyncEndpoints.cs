using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Orders;

public static class OrderSyncEndpoints
{
    private const string OrderSyncRoute = "/api/erp/shops/{sid:int}/order-sync";
    private const string SettingsRoute = "/api/erp/order-sync-settings";

    public static void MapOrderSync(this WebApplication app)
    {
        app.MapPost(OrderSyncRoute, async Task<Results<Accepted<OrderSyncStatus>, ProblemHttpResult>>
            (int sid, IErpTokenStore tokens, OrderSyncJobs jobs, CancellationToken cancellationToken) =>
        {
            if (sid <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须为正整数");
            var configuration = await tokens.GetConfigurationAsync(cancellationToken);
            if (configuration.IsErr)
                return TypedResults.Problem(statusCode: 503, title: configuration.Error.Message);
            if (!configuration.Value.Configured)
                return TypedResults.Problem(statusCode: 503, title: "尚未配置私有 ERP AccessToken");
            var started = jobs.Start(sid);
            if (started.IsErr)
                return TypedResults.Problem(statusCode: 409, title: started.Error.Message);

            return TypedResults.Accepted($"/api/erp/shops/{sid}/order-sync", started.Value);
        }).WithSummary("为 TemuPop 店铺启动历史订单同步");

        app.MapGet(OrderSyncRoute, Results<Ok<OrderSyncStatus>, NoContent, ProblemHttpResult>
            (int sid, OrderSyncJobs jobs) =>
        {
            if (sid <= 0)
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须为正整数");
            if (jobs.Latest(sid) is { } job)
                return TypedResults.Ok(job);

            return TypedResults.NoContent();
        }).WithSummary("查询店铺最近一次历史订单同步");

        app.MapGet(SettingsRoute, async Task<Results<Ok<OrderSyncSettingsResponse>, ProblemHttpResult>>
            (IOrderSyncSettingsStore store, IOptions<OrderSyncSchedulerOptions> options, CancellationToken cancellationToken) =>
        {
            var settings = await store.GetAsync(cancellationToken);
            return TypedResults.Ok(ToResponse(settings, options.Value));
        }).WithSummary("读取定时同步设置；周期只读，由服务端配置");

        app.MapPut(SettingsRoute, async Task<Results<Ok<OrderSyncSettingsResponse>, ProblemHttpResult>>
            (OrderSyncSettingsRequest request, IOrderSyncSettingsStore store, OrderSyncScheduler scheduler,
                IErpTokenStore tokens, IOptions<OrderSyncSchedulerOptions> options, CancellationToken cancellationToken) =>
        {
            if (request.Enabled is null || request.ShopSids is null)
                return TypedResults.Problem(statusCode: 400, title: "缺少定时同步设置字段");
            if (request.Enabled.Value && request.ShopSids.Count == 0)
                return TypedResults.Problem(statusCode: 400, title: "启用定时同步前请至少选择一家店铺");
            if (request.ShopSids.Any(sid => sid <= 0))
                return TypedResults.Problem(statusCode: 400, title: "店铺 SID 必须为正整数");
            if (request.Enabled.Value)
            {
                var configuration = await tokens.GetConfigurationAsync(cancellationToken);
                if (configuration.IsErr)
                    return TypedResults.Problem(statusCode: 503, title: configuration.Error.Message);
                if (!configuration.Value.Configured)
                    return TypedResults.Problem(statusCode: 503, title: "启用定时同步前请先配置私有 ERP AccessToken");
            }

            var saved = await store.SaveAsync(request.Enabled.Value, request.ShopSids, cancellationToken);
            if (saved.IsErr)
                return TypedResults.Problem(statusCode: 500, title: saved.Error.Message);

            scheduler.NotifySettingsChanged();
            return TypedResults.Ok(ToResponse(saved.Value, options.Value));
        }).WithSummary("保存定时同步的启用状态与店铺选择，并立即通知调度器");

        app.MapGet($"{SettingsRoute}/shops", async Task<Results<Ok<OrderSyncShopSelection>, ProblemHttpResult>>
            (IErpClient erp, IErpTokenStore tokens, CancellationToken cancellationToken) =>
        {
            var token = await tokens.GetAsync(cancellationToken);
            if (token.IsErr)
                return TypedResults.Problem(statusCode: 503, title: token.Error.Message);

            var shops = await erp.GetAllShopsAsync("TemuPop", token.Value, cancellationToken);
            if (shops.IsErr)
                return TypedResults.Problem(statusCode: 502, title: "获取店铺失败", detail: shops.Error.Message);

            return TypedResults.Ok(new OrderSyncShopSelection(
                shops.Value.Select(shop => new OrderSyncShopOption(shop.Sid, shop.Name)).ToArray()));
        }).WithSummary("列出 Token 可见的全部店铺供定时同步选店，不查询授权状态");
    }

    private static OrderSyncSettingsResponse ToResponse(OrderSyncSettingsSnapshot settings, OrderSyncSchedulerOptions options) =>
        new(settings.Enabled, settings.ShopSids, options.SchedulerIntervalMinutes);
}

public sealed record OrderSyncSettingsRequest(bool? Enabled, IReadOnlyList<int>? ShopSids);

public sealed record OrderSyncSettingsResponse(bool Enabled, IReadOnlyList<int> ShopSids, int IntervalMinutes);

public sealed record OrderSyncShopOption(string Sid, string Name);

public sealed record OrderSyncShopSelection(IReadOnlyList<OrderSyncShopOption> Items);
