using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderStoreLocalCacheTests
{
    [Fact]
    public async Task Scheduled_sync_without_Redis_replays_persisted_Sqlite_orders_as_unchanged_after_restart()
    {
        // Given 只配置 SQLite 和内存缓存，已选中一家店，ERP 返回两张带明细的订单。
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("openerp-order-cache-");
        try
        {
            var database = Path.Combine(directory.FullName, "orders.db");
            const int shopSid = 31;
            var first = new OrderSnapshot("erp-001", "order-1", shopSid, TestPlatformOrderSource.DefaultPlatform, "pending",
                new(new("1700000000", "addTime"), null, null, null),
                [new("po-1", shopSid, "1", "待发货")], [new("product-1", "oguid-1", "sku-id-1", "sku-1", 3, false, false)]);
            OrderSnapshot[] orders = [first, first with { ErpId = "erp-002", OrderNumber = "order-2" }];
            var source = new TestPlatformOrderSource((shop, _, _) => Task.FromResult(OpResults.Ok(new OrderImportBatch(
                2, shop.Sid == shopSid ? orders : [], new Dictionary<string, int>()))));
            const string probe = "test:local-cache";
            await using (var initial = CreateServices(database, source))
            {
                Assert.False(initial.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IDistributedCache)));
                var cache = initial.GetRequiredService<HybridCache>();
                await cache.SetAsync(probe, "initial", cancellationToken: ct);
                Assert.Equal("initial", await cache.GetOrCreateAsync(probe,
                    static _ => ValueTask.FromResult("cold"), cancellationToken: ct));
                await using (var scope = initial.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await db.MigrateDevelopmentDatabaseAsync(ct);
                    var settings = await scope.ServiceProvider.GetRequiredService<IOrderSyncSettingsStore>()
                        .SaveAsync(true, [shopSid], ct);
                    if (settings.IsErr)
                        throw new XunitException(settings.Error.Message);
                }

                // When 调度器执行一轮；Then 后台同步保存两张订单。
                Assert.Equal(new OrderSaveCounts(2, 0, 0), await RunScheduledCycleAsync(initial, shopSid, ct));
            }

            // When 旧宿主释放，新宿主以空缓存和原 SQLite 文件执行下一轮。
            await using var replay = CreateServices(database, source);
            var replayCache = replay.GetRequiredService<HybridCache>();
            Assert.Equal("cold", await replayCache.GetOrCreateAsync(probe,
                static _ => ValueTask.FromResult("cold"), cancellationToken: ct));

            // Then 两张订单都计为未变，主单、平台子单与商品明细依然存在。
            Assert.Equal(new OrderSaveCounts(0, 0, 2), await RunScheduledCycleAsync(replay, shopSid, ct));
            await using var replayScope = replay.CreateAsyncScope();
            var stored = await replayScope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
                .AsNoTracking().Include(order => order.PlatformOrders).Include(order => order.Lines)
                .OrderBy(order => order.ErpId).ToArrayAsync(ct);
            Assert.Equal(orders.Select(order => order.ErpId), stored.Select(order => order.ErpId));
            Assert.All(stored, order =>
            {
                Assert.Equal("po-1", Assert.Single(order.PlatformOrders).PoNumber);
                Assert.Equal(3, Assert.Single(order.Lines).Quantity);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<OrderSaveCounts> RunScheduledCycleAsync(ServiceProvider services, int shopSid, CancellationToken ct)
    {
        var jobs = services.GetRequiredService<OrderSyncJobs>();
        try
        {
            await jobs.StartAsync(ct);
            var scheduler = services.GetRequiredService<OrderSyncScheduler>();
            Assert.Equal(OrderSyncCycleOutcome.Started, await scheduler.RunCycleAsync(ct));
            await TestWait.UntilAsync(() => jobs.Latest(shopSid) is { Status: "succeeded" or "partial" or "failed" },
                "定时订单同步未完成。", ct);
            var status = Assert.IsType<OrderSyncStatus>(jobs.Latest(shopSid));
            Assert.True(status.Status == "succeeded", $"定时同步状态：{status.Status}，错误：{status.Error}");
            return new(status.Added, status.Updated, status.Unchanged);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await jobs.StopAsync(cleanup.Token);
        }
    }

    private static ServiceProvider CreateServices(string path, TestPlatformOrderSource source)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["Database:Provider"] = "SQLite";
        builder.Configuration["ConnectionStrings:openerp"] = $"Data Source={path};Pooling=False";
        builder.AddApplicationDatabase();
        builder.AddApplicationCache();
        builder.Services.AddScoped<OrderStore>();
        builder.Services.AddScoped<IOrderSyncSettingsStore, OrderSyncSettingsStore>();
        builder.Services.AddSingleton<IPlatformShops>(new TestPlatformShops());
        builder.Services.AddSingleton(new PlatformOrderSources([source]));
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        return builder.Services.BuildServiceProvider();
    }
}
