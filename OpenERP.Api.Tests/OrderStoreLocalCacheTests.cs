using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderStoreLocalCacheTests
{
    [Fact]
    public async Task Restart_without_Redis_replays_persisted_Sqlite_orders_as_unchanged()
    {
        // Given 本机只配置 SQLite 文件，使用该宿主的内存缓存保存两张带明细的订单。
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("openerp-order-cache-");
        try
        {
            var database = Path.Combine(directory.FullName, "orders.db");
            var first = new OrderSnapshot("erp-001", "order-1", 31, "TemuPop", "pending",
                new(new("1700000000", "addTime"), null, null, null),
                [new("po-1", 31, "1", "待发货")], [new("product-1", "oguid-1", "sku-id-1", "sku-1", 3, false, false)]);
            OrderSnapshot[] orders = [first, first with { ErpId = "erp-002", OrderNumber = "order-2" }];
            const string probe = "test:local-cache";
            await using (var initial = CreateServices(database))
            {
                Assert.False(initial.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IDistributedCache)));
                var cache = initial.GetRequiredService<HybridCache>();
                await cache.SetAsync(probe, "initial", cancellationToken: ct);
                Assert.Equal("initial", await cache.GetOrCreateAsync(probe,
                    static _ => ValueTask.FromResult("cold"), cancellationToken: ct));
                await using var scope = initial.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.MigrateDevelopmentDatabaseAsync(ct);
                var saved = await scope.ServiceProvider.GetRequiredService<OrderStore>().SaveAsync(orders, ct);
                if (saved.IsErr)
                    throw new XunitException(saved.Error.Message);
                Assert.Equal(new OrderSaveCounts(2, 0, 0), saved.Value);
            }

            // When 旧宿主释放，新宿主缓存为空，对同一个 SQLite 文件回放订单。
            await using var replay = CreateServices(database);
            var replayCache = replay.GetRequiredService<HybridCache>();
            Assert.Equal("cold", await replayCache.GetOrCreateAsync(probe,
                static _ => ValueTask.FromResult("cold"), cancellationToken: ct));
            await using var replayScope = replay.CreateAsyncScope();
            var replayed = await replayScope.ServiceProvider.GetRequiredService<OrderStore>().SaveAsync(orders, ct);

            // Then 两张订单都计为未变，主单、平台子单与商品明细依然存在。
            if (replayed.IsErr)
                throw new XunitException(replayed.Error.Message);
            Assert.Equal(new OrderSaveCounts(0, 0, 2), replayed.Value);
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

    private static ServiceProvider CreateServices(string path)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["Database:Provider"] = "SQLite";
        builder.Configuration["ConnectionStrings:openerp"] = $"Data Source={path};Pooling=False";
        builder.AddApplicationDatabase();
        builder.AddApplicationCache();
        builder.Services.AddScoped<OrderStore>();
        return builder.Services.BuildServiceProvider();
    }
}
