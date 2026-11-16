using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSyncScheduledPostgresTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_scheduled_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _testConnectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(cancellationToken);
        _testConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _databaseCreated = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseCreated)
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""DROP DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Scheduled_batch_saves_orders_and_replays_without_new_rows()
    {
        // Given 定时同步选中一家店，ERP 返回一张可保存的订单。
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_testConnectionString, builder => builder.EnableRetryOnFailure().MaxBatchSize(1))
            .Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync(cancellationToken);

        var order = new OrderSnapshot("erp-1", "shared-number", 30, "TemuPop", "pending",
            new(new("1700000000", "addTime"), null, null, null),
            [new("po-1", 30, "1", "待发货")],
            [new("product-1", "oguid-1", "sku-id-1", "sku-1", 3, null, null)]);
        var erp = new TestErpClient
        {
            ReadOrders = (_, _) => Task.FromResult(OpResults.Ok(new OrderImportBatch(
                1, [order], new Dictionary<string, int>())))
        };

        var services = new ServiceCollection();
        services.AddSingleton<IErpClient>(erp);
        services.AddScoped(_ => new OrderStore(new AppDbContext(options)));
        using var provider = services.BuildServiceProvider();
        using var jobs = new OrderSyncJobs(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderSyncJobs>.Instance);
        try
        {
            await jobs.StartAsync(cancellationToken);

            // When 第一次执行定时批次。
            await TestWait.EventuallyAsync(
                () => jobs.StartScheduledBatch([30]), "首次定时批次未能受理。", cancellationToken);
            await TestWait.UntilAsync(
                () => jobs.Latest(30)?.Status == "succeeded",
                "首次定时同步没有完成。", cancellationToken);

            // Then 订单写入一次。
            Assert.Equal(1, jobs.Latest(30)?.Added);
            Assert.Equal(1L, await CountAsync("Orders", cancellationToken));

            // When 再次执行同一批次的恢复扫描。
            await TestWait.EventuallyAsync(
                () => jobs.StartScheduledBatch([30]), "重复定时批次未能受理。", cancellationToken);
            await TestWait.UntilAsync(
                () => jobs.Latest(30) is { Status: "succeeded", Added: 0, Updated: 0, Unchanged: 1 },
                "重复定时同步没有按幂等结果完成。", cancellationToken);

            // Then 主单、平台子单和商品行都没有重复写入。
            Assert.Equal(1L, await CountAsync("Orders", cancellationToken));
            Assert.Equal(1L, await CountAsync("PlatformOrders", cancellationToken));
            Assert.Equal(1L, await CountAsync("OrderLines", cancellationToken));
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    private async Task<long> CountAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""SELECT count(*) FROM "{table}" """, connection);
        return Assert.IsType<long>(await command.ExecuteScalarAsync(cancellationToken));
    }
}
