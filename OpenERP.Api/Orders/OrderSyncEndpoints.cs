using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders.Sources;

namespace OpenERP.Api.Orders;

public static class OrderSyncEndpoints
{
    /// <summary>订单同步页的店铺列表与平台凭据管理；店铺来自授权店铺目录，所有环境都可查看。</summary>
    public static void MapPlatformShops(this WebApplication app)
    {
        var orderSyncEnabled = app.Environment.IsDevelopment() || app.Environment.IsProduction();
        app.MapGet("/api/order-sync/availability", async Task<Results<Ok<OrderSyncAvailability>, ProblemHttpResult>> (
                PlatformOrderSources sources, IErpTokenStore tokens, CancellationToken cancellationToken) =>
            {
                var configuration = await tokens.GetConfigurationAsync(cancellationToken);
                return configuration.IsErr
                    ? TypedResults.Problem(statusCode: 503, title: configuration.Error.Message)
                    : TypedResults.Ok(new OrderSyncAvailability(
                        configuration.Value.Configured, orderSyncEnabled, sources.Platforms.Order().ToArray()));
            })
            .RequireAuthorization()
            .WithSummary("查询 ERP 是否已配置、订单同步是否可用及已实现订单同步的平台");

        var shops = app.MapGroup("/api/order-sync/shops").RequireAuthorization();

        shops.MapGet("", async Task<Results<Ok<OrderSyncShopPage>, ProblemHttpResult>> (
            [FromServices] AppDbContext db, PlatformOrderSources sources, System.Security.Claims.ClaimsPrincipal user,
            int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            var currentPage = page ?? 1;
            var currentSize = pageSize ?? 20;
            if (currentPage < 1 || currentSize is < 1 or > 100)
                return TypedResults.Problem("page 必须大于 0，pageSize 必须在 1 到 100 之间。", statusCode: 400);

            try
            {
                var query = ShopAccess.For(user).Shops(db).AsNoTracking();
                var total = await query.CountAsync(cancellationToken);
                var pageCount = total == 0 ? 0 : (total + currentSize - 1) / currentSize;
                currentPage = pageCount == 0 ? 1 : Math.Min(currentPage, pageCount);
                var rows = await query.OrderBy(shop => shop.Name).ThenBy(shop => shop.Sid)
                    .Skip((currentPage - 1) * currentSize).Take(currentSize)
                    .Select(shop => new { shop.Sid, shop.Name, shop.Platform, shop.Region })
                    .ToArrayAsync(cancellationToken);
                var items = rows.Select(row => new OrderSyncShopItem(row.Sid, row.Name, row.Platform, row.Region,
                    sources.Find(row.Platform) is not null)).ToArray();
                return TypedResults.Ok(new OrderSyncShopPage(items, currentPage, currentSize, total, pageCount));
            }
            catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
            {
                return TypedResults.Problem("本地店铺数据暂时不可用。", statusCode: 503);
            }
        }).WithSummary("分页读取可见的授权店铺，并标明平台是否已实现订单同步；员工只看到分配给自己的店铺");

    }

    public static void MapOrderSync(this WebApplication app)
    {
        var settingsEndpoints = app.MapGroup("/api/order-sync/settings")
            .RequireAuthorization(UserAccount.AdministratorRole);

        settingsEndpoints.MapGet("", async Task<Results<Ok<OrderSyncSettingsResponse>, ProblemHttpResult>>
            (IOrderSyncSettingsStore store, IOptions<OrderSyncSchedulerOptions> options, CancellationToken cancellationToken) =>
        {
            var settings = await store.GetAsync(cancellationToken);
            return TypedResults.Ok(ToResponse(settings, options.Value));
        }).WithSummary("读取定时同步设置；周期只读，由服务端配置");

        settingsEndpoints.MapPut("", async Task<Results<Ok<OrderSyncSettingsResponse>, ProblemHttpResult>>
            (OrderSyncSettingsRequest request, IOrderSyncSettingsStore store, OrderSyncScheduler scheduler,
                IOptions<OrderSyncSchedulerOptions> options, CancellationToken cancellationToken) =>
        {
            if (request.Enabled is null || request.ShopSids is null)
                return TypedResults.Problem(statusCode: 400, title: "缺少定时同步设置字段");
            if (request.Enabled.Value && request.ShopSids.Count == 0)
                return TypedResults.Problem(statusCode: 400, title: "启用定时同步前请至少选择一家店铺");
            if (request.ShopSids.Any(sid => sid <= 0))
                return TypedResults.Problem(statusCode: 400, title: "店铺编号必须为正整数");

            var saved = await store.SaveAsync(request.Enabled.Value, request.ShopSids, cancellationToken);
            if (saved.IsErr)
                return TypedResults.Problem(statusCode: 500, title: saved.Error.Message);

            scheduler.NotifySettingsChanged();
            return TypedResults.Ok(ToResponse(saved.Value, options.Value));
        }).WithSummary("保存定时同步的启用状态与店铺选择，并立即通知调度器");

        settingsEndpoints.MapGet("/shops", async Task<Results<Ok<OrderSyncShopSelection>, ProblemHttpResult>>
            ([FromServices] AppDbContext db, PlatformOrderSources sources, CancellationToken cancellationToken) =>
        {
            try
            {
                var platforms = sources.Platforms.ToArray();
                var shops = await db.Shops.AsNoTracking()
                    .Where(shop => platforms.Contains(shop.Platform))
                    .OrderBy(shop => shop.Name).ThenBy(shop => shop.Sid)
                    .Select(shop => new OrderSyncShopOption(shop.Sid.ToString(), shop.Name))
                    .ToArrayAsync(cancellationToken);
                return TypedResults.Ok(new OrderSyncShopSelection(shops));
            }
            catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
            {
                return TypedResults.Problem(statusCode: 503, title: "本地店铺数据暂时不可用。");
            }
        }).WithSummary("读取已实现订单同步的平台下的全部授权店铺供定时同步选店");
    }

    private static OrderSyncSettingsResponse ToResponse(OrderSyncSettingsSnapshot settings, OrderSyncSchedulerOptions options) =>
        new(settings.Enabled, settings.ShopSids, options.SchedulerIntervalMinutes);
}

public sealed record OrderSyncAvailability(bool Configured, bool OrderSyncEnabled, IReadOnlyList<string> Platforms);

public sealed record OrderSyncShopItem(
    int Sid, string Name, string Platform, string? Region, bool SyncSupported);

public sealed record OrderSyncShopPage(
    IReadOnlyList<OrderSyncShopItem> Items, int Page, int PageSize, int TotalCount, int PageCount);

public sealed record OrderSyncSettingsRequest(bool? Enabled, IReadOnlyList<int>? ShopSids);

public sealed record OrderSyncSettingsResponse(bool Enabled, IReadOnlyList<int> ShopSids, int IntervalMinutes);

public sealed record OrderSyncShopOption(string Sid, string Name);

public sealed record OrderSyncShopSelection(IReadOnlyList<OrderSyncShopOption> Items);
