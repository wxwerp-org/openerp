using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;
using OpenERP.Api.Reports;
using OpenERP.Core.Reports;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ReadModelMigrationTests(AspireIntegrationFixture fixture)
{
    [Fact]
    public async Task Postgres_upgrade_keeps_existing_orders_and_shops_readable_without_resync()
    {
        // Given 旧版本已保存一张包含上游墙钟时间的订单和对应店铺。
        var ct = TestContext.Current.CancellationToken;
        var connection = new NpgsqlConnectionStringBuilder(await fixture.GetPostgresConnectionStringAsync(ct))
        {
            Database = $"openerp_read_model_{Guid.NewGuid():N}",
            Pooling = false
        }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(connection).Options);
        try
        {
            const string previousMigration = "20261122062150_UserShopAssignments";
            await db.GetService<IMigrator>().MigrateAsync(previousMigration, ct);
            const string created = "17-11-2026 00:01:02.654321+08:00";
            await using (var legacy = new LegacyReadContext(new DbContextOptionsBuilder<LegacyReadContext>().UseNpgsql(connection).Options))
            {
                legacy.Add(new LegacyOrder { CreatedRaw = created });
                legacy.Add(new LegacyShop());
                await legacy.SaveChangesAsync(ct);
            }

            // When 按启动顺序应用迁移并回填两次；Then 日期查询、报表与店名可用，保存的原始时间不变。
            await db.MigrateDevelopmentDatabaseAsync(ct);
            await ReadModelBackfill.RunAsync(db, ct);
            await ReadModelBackfill.RunAsync(db, ct);

            var day = new DateOnly(2026, 11, 17);
            var access = ShopAccess.For(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, UserAccount.AdministratorRole)], "Test")));
            var (listed, _) = await new OrderListQuery(db, NullLogger<OrderListQuery>.Instance)
                .ReadAsync(new(30, "TemuPop", null, null, day, day, 1, 10), access, ct);
            if (listed.IsErr) throw new XunitException(listed.Error.Message);
            var item = Assert.Single(listed.Value.Items);
            Assert.Equal("legacy", item.Id);
            Assert.Equal("2026-11-17T00:01:02.654321+08:00", item.Created);
            Assert.Equal("Équipe", item.ShopName);

            var report = await new OrderSnapshotReportQuery(db, TimeProvider.System).QueryAsync(
                new(null, [], TrendStart: day, TrendEnd: day), ct);
            if (report.IsErr) throw new XunitException(report.Error.Message);
            Assert.Equal(new OrderTrendPoint("2026-11-17", 1, 0, 1.23m), Assert.Single(report.Value.Trend.Points));
            Assert.Equal(created, await db.Orders.Select(order => order.CreatedRaw).SingleAsync(ct));
            Assert.Equal("ÉQUIPE", await db.Shops.Select(shop => shop.SearchName).SingleAsync(ct));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }

    private sealed class LegacyReadContext(DbContextOptions<LegacyReadContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<LegacyOrder>().ToTable("Orders");
            builder.Entity<LegacyShop>().ToTable("Shops");
        }
    }

    private sealed class LegacyOrder
    {
        public Guid Id { get; init; } = Guid.CreateVersion7();
        public string ErpId { get; init; } = "legacy";
        public string OrderNumber { get; init; } = "legacy";
        public int Sid { get; init; } = 30;
        public string Platform { get; init; } = "TemuPop";
        public string Status { get; init; } = "正常";
        public string? CreatedRaw { get; init; }
        public decimal IncomeCost { get; init; } = 1.23m;
        public string IncomeCurrency { get; init; } = "USD";
    }

    private sealed class LegacyShop
    {
        public Guid Id { get; init; } = Guid.CreateVersion7();
        public int Sid { get; init; } = 30;
        public string Name { get; init; } = "Équipe";
        public string Platform { get; init; } = "TemuPop";
    }
}
