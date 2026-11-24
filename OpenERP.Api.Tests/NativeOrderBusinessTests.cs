using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpResult;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;
using OpenERP.Api.Reports;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class NativeOrderBusinessTests : IAsyncLifetime
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"openerp-business-{Guid.NewGuid():N}");
    private string ConnectionString => $"Data Source={Path.Join(_directory, "orders.sqlite")};Pooling=False;Foreign Keys=True;Default Timeout=1";
    private SqliteAppDbContext Open() => new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(ConnectionString).Options);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        await using var db = Open();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Order_list_keeps_source_precision_and_filters_by_wall_day()
    {
        // Given 同日的偏移时间、UTC 秒数、歧义日期与非法日期，When 按日期筛选，Then 只返回当日有效记录。
        await SeedAsync();
        await using var db = Open();
        var query = new OrderListQuery(db, new SqliteOrderReadSource(db), NullLogger<OrderListQuery>.Instance);
        var admin = ShopAccess.For(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, UserAccount.AdministratorRole)], "test")));
        var day = new DateOnly(2026, 11, 14);
        var result = await query.ReadAsync(new(null, null, null, null, day, day, 1, 20), admin, Ct);
        Assert.Equal(200, result.Status);
        if (result.Result.IsErr) throw new XunitException(result.Result.Error.Message);
        Assert.Equal(["offset", "epoch"], result.Result.Value.Items.Select(order => order.Id).ToArray());
        Assert.Equal("2026-11-14T23:59:59.999999-05:00", result.Result.Value.Items[0].Created);
        Assert.Equal("2026-11-14T00:00:00Z", result.Result.Value.Items[1].Created);
    }

    [Fact]
    public async Task Report_groups_exact_currency_and_counts_unusable_source_dates()
    {
        // Given 两笔精确金额与两笔无效日期，When 汇总报表，Then 日期、金额和商品件数一致。
        await SeedAsync();
        await using var db = Open();
        var day = new DateOnly(2026, 11, 14);
        var result = await new OrderSnapshotReportQuery(db, new SqliteOrderReadSource(db), TimeProvider.System)
            .QueryAsync(new(null, [], TrendStart: day, TrendEnd: day), Ct);
        if (result.IsErr) throw new XunitException(result.Error.Message);
        Assert.Equal(.01m, result.Value.Sales.Amount);
        Assert.Equal(2, result.Value.Trend.MissingCreatedCount);
        Assert.Equal(4, result.Value.Summary.OrderCount);
        Assert.Equal(2, Assert.Single(result.Value.Trend.Points).OrderCount);
        Assert.Equal(8, result.Value.Summary.ItemQuantity);
    }

    [Fact]
    public async Task Income_rounds_each_row_and_rejects_the_whole_out_of_range_batch()
    {
        // Given PG numeric(18,2) 的正负中点，When 同步并重放，Then 每笔按 DB 规则存储且不会重复更新。
        await using var db = Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrderFingerprintCache();
        await using var provider = services.BuildServiceProvider();
        var store = ActivatorUtilities.CreateInstance<OrderStore>(provider, db);
        OrderSnapshot[] orders = [Order("a", "2026-11-14", .105m), Order("b", "2026-11-14", -.105m)];
        Assert.True((await store.SaveAsync(orders, Ct)).IsOk);
        db.ChangeTracker.Clear();
        Assert.Equal([.11m, -.11m], await db.Orders.OrderBy(o => o.ErpId).Select(o => o.IncomeCost!.Value).ToArrayAsync(Ct));
        var replay = await store.SaveAsync(orders, Ct);
        if (replay.IsErr) throw new XunitException(replay.Error.Message);
        Assert.Equal(2, replay.Value.Unchanged);
        Assert.True((await store.SaveAsync([Order("c", "", 1), Order("d", "", 9999999999999999.995m)], Ct)).IsErr);
        // 失败后的同一上下文还要能继续用于后续批次，不能在下次提交时偷偷插入 c。
        Assert.True((await store.SaveAsync([Order("e", "", 2)], Ct)).IsOk);
        Assert.Equal(["a", "b", "e"], await db.Orders.OrderBy(o => o.ErpId).Select(o => o.ErpId).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task Shop_search_folds_case_and_treats_wildcards_as_literal_text()
    {
        // Given 店名带非 ASCII 大小写、百分号和下划线，When 按原文搜索，Then 通配符不会扩大结果。
        var employee = new UserAccount { Username = "employee", Name = "员工" };
        await using (var db = Open())
        {
            db.Users.Add(employee);
            db.UserShopAssignments.Add(new UserShopAssignment { UserId = employee.Id, Sid = 1, Platform = "Test" });
            db.Shops.AddRange(new Shop { Sid = 1, Platform = "Test", Name = "Été%_A" },
                new Shop { Sid = 2, Platform = "Test", Name = "étéZZa" });
            await db.SaveChangesAsync(Ct);
        }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSqlitePersistence((_, options) => options.UseSqlite(ConnectionString));
        ErpEndpointTestAuth.Configure(builder);
        await using var app = builder.Build();
        app.MapShopDirectory();
        await app.StartAsync(Ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.AdministratorRole);
        var matches = await http.GetFromJsonAsync<ShopDirectoryPage>("/api/erp/shops?keyword=" + Uri.EscapeDataString("été%_"), Ct);
        Assert.Equal("1", Assert.Single(matches!.Items).Sid);
        Assert.Equal("2", Assert.Single((await http.GetFromJsonAsync<ShopDirectoryPage>("/api/erp/shops?keyword=2", Ct))!.Items).Sid);
        using var restricted = ErpEndpointTestAuth.CreateClient(app, UserAccount.UserRole, employee.Id);
        var visible = await restricted.GetFromJsonAsync<EmployeeShopDirectoryPage>("/api/erp/shops?keyword=" + Uri.EscapeDataString("été"), Ct);
        Assert.Equal("1", Assert.Single(visible!.Items).Sid);
        Assert.Empty((await restricted.GetFromJsonAsync<EmployeeShopDirectoryPage>("/api/erp/shops?keyword=2", Ct))!.Items);
    }

    [Fact]
    public async Task Report_keeps_its_snapshot_while_another_connection_changes_orders()
    {
        // Given WAL 中四笔订单，When 报表首次读取后另一连接删除订单，Then 写入成功且整份报表仍看到同一快照。
        await SeedAsync();
        var writer = new ConcurrentOrderWriter(ConnectionString);
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(ConnectionString).AddInterceptors(writer).Options;
        await using var db = new SqliteAppDbContext(options);
        var report = await new OrderSnapshotReportQuery(db, new SqliteOrderReadSource(db), TimeProvider.System).QueryAsync(new(null, []), Ct);
        Assert.True(writer.Committed);
        if (report.IsErr) throw new XunitException(report.Error.Message);
        Assert.Equal(4, report.Value.Summary.OrderCount);
        Assert.Equal(4, report.Value.Orders.Count);
        Assert.False(await db.Orders.AnyAsync(Ct));
    }

    private sealed class ConcurrentOrderWriter(string connectionString) : DbCommandInterceptor
    {
        public bool Committed { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Committed && command.CommandText.Contains("FROM \"Orders\"", StringComparison.Ordinal))
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                using var write = connection.CreateCommand();
                write.CommandText = "DELETE FROM \"Orders\"";
                await write.ExecuteNonQueryAsync(cancellationToken);
                Committed = true;
            }
            return result;
        }
    }

    [Fact]
    public async Task Shop_sync_reads_saved_configuration_and_persists_authorization_and_settings()
    {
        // Given 真实本机存储与合成 ERP 响应，When 完成一轮门店同步并重开上下文，Then 目录与调度设置可用。
        var services = new ServiceCollection();
        services.AddSqlitePersistence((_, options) => options.UseSqlite(ConnectionString));
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddScoped<IErpTokenStore, ErpTokenStore>();
        services.AddSingleton<IErpClient>(new TestErpClient
        {
            GetAllShops = (_, _, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>(
                [new("42", "同步店", "Test", "US", null)])),
            GetShopStatus = (_, _, _) => Task.FromResult(OpResults.Ok(new ErpShopStatus(true, "US", true)))
        });
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IErpTokenStore>().SaveAsync("test-only", Ct)).IsOk);
            var settings = new OrderSyncSettingsStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            Assert.True((await settings.SaveAsync(true, [42, 42], Ct)).IsOk);
        }

        var finished = new CompletionNotifier();
        var job = new ShopSyncJob(provider.GetRequiredService<IServiceScopeFactory>(), finished,
            TimeProvider.System, NullLogger<ShopSyncJob>.Instance);
        var started = await job.TryStartAsync(Ct);
        if (started.IsErr) throw new XunitException(started.Error.Message);
        var final = await finished.Result.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(final.Error);
        await using var read = Open();
        var shop = await read.Shops.SingleAsync(Ct);
        Assert.Equal(42, shop.Sid);
        Assert.True(shop.IsTokenOk);
        Assert.NotNull(shop.SyncedAt);
        var persisted = await new OrderSyncSettingsStore(read).GetAsync(Ct);
        Assert.True(persisted.Enabled);
        Assert.Equal([42], persisted.ShopSids);
        Assert.Equal(final.Version, await read.ShopSyncStates.Select(state => state.Version).SingleAsync(Ct));
    }

    private sealed class CompletionNotifier : IShopSyncNotifier
    {
        internal TaskCompletionSource<ShopSyncStatus> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PublishAsync(ShopSyncStatus status, CancellationToken cancellationToken)
        {
            if (!status.Running)
                Result.TrySetResult(status);
            return Task.CompletedTask;
        }
    }

    private async Task SeedAsync()
    {
        await using var db = Open();
        var result = await new OrderStore(db).SaveAsync([
            Order("offset", "2026-11-14T23:59:59.999999-05:00", .105m),
            Order("epoch", "1794614400", -.104m), Order("ambiguous", "12-11-2026", null),
            Order("invalid", "2026-02-30", null)], Ct);
        if (result.IsErr) throw new XunitException(result.Error.Message);
    }

    private static OrderSnapshot Order(string id, string created, decimal? amount) =>
        new(id, id, 1, "Test", "pending", new(new(created, "fixture"), null, null, null),
            [new(id, 1, null, null)], [new("p", "o", "s", "code", 2, false, false)], amount, "USD");
}
