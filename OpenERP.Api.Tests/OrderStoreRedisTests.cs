using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderStoreRedisTests(AspireIntegrationFixture fixture)
{
    [Fact]
    public async Task Replay_reads_redis_from_a_new_service_provider_and_skips_order_children()
    {
        // Given 独立数据库、真实 Redis，以及带平台子单和商品行的订单。
        var ct = TestContext.Current.CancellationToken;
        var postgres = new NpgsqlConnectionStringBuilder(await fixture.GetPostgresConnectionStringAsync(ct))
        {
            Database = $"openerp_redis_orders_{Guid.NewGuid():N}",
            Pooling = false
        };
        var redis = await fixture.GetRedisConnectionStringAsync(ct);
        var sid = Random.Shared.Next(1_000_000_000, int.MaxValue);
        var order = new OrderSnapshot(Guid.NewGuid().ToString("N"), "order-1", sid, "TemuPop", "pending",
            new(new("1700000000", "addTime"), null, null, null),
            [new("po-1", sid, "1", "待发货")], [new("product-1", "oguid-1", "sku-id-1", "sku-1", 3, false, false)]);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using var initial = new AppDbContext(options);
        try
        {
            await initial.Database.MigrateAsync(ct);
            Assert.Equal(new OrderSaveCounts(1, 0, 0), await SaveAsync(initial, redis, order, ct));

            // When 原服务容器已释放，新容器的内存缓存为空。
            var commands = new List<string>();
            var replayOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString)
                .LogTo(commands.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information).Options;
            await using var replay = new AppDbContext(replayOptions);
            var result = await SaveAsync(replay, redis, order, ct);

            // Then 回放仍命中缓存，只需通过 PostgreSQL 核对主单同步版本。
            Assert.Equal(new OrderSaveCounts(0, 0, 1), result);
            Assert.Contains(commands, sql => sql.Contains("\"SyncRevision\"", StringComparison.Ordinal));
            Assert.DoesNotContain(commands, sql => sql.Contains("\"PlatformOrders\"", StringComparison.Ordinal) ||
                sql.Contains("\"OrderLines\"", StringComparison.Ordinal));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await initial.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }

    private static async Task<OrderSaveCounts> SaveAsync(AppDbContext db, string redis, OrderSnapshot order,
        CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["ConnectionStrings:redis"] = redis;
        builder.AddRedisDistributedCache("redis");
        builder.Services.AddHybridCache();
        using var host = builder.Build();
        var store = ActivatorUtilities.CreateInstance<OrderStore>(host.Services, db);
        var result = await store.SaveAsync([order], cancellationToken);
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        return result.Value;
    }
}
