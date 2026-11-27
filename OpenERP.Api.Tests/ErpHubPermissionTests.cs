using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpResult;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
using Xunit;

namespace OpenERP.Api.Tests;

/// <summary>ERP Hub 的管理员校验与按角色分发事件。</summary>
[Collection(nameof(AspireIntegrationCollection))]
public sealed class ErpHubPermissionTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_hub_permission_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _connectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(cancellationToken);
        _connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
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
    public async Task Shop_sync_snapshot_is_sent_only_to_administrator_connections()
    {
        // Given 管理员与员工各一条已登录连接，目录同步任务空闲。
        var ct = TestContext.Current.CancellationToken;
        var (adminLogin, employeeLogin) = await SeedAccountsAsync(ct);
        var job = new RecordingShopSyncJob();
        await using var app = await StartAppAsync(job, orderSync: false, ct);
        var adminEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        var employeeEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var admin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var adminSubscription = admin.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => adminEvents.Writer.TryWrite(status));
        using var employeeSubscription = employee.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => employeeEvents.Writer.TryWrite(status));

        // When 管理员与员工分别连接。
        await admin.StartAsync(ct);
        await employee.StartAsync(ct);

        // Then 目录同步初始快照只发给管理员；员工重连后仍然收不到。
        Assert.Equal(job.Status, await ReadAsync(adminEvents, ct));
        await AssertNoMessageAsync(employeeEvents, ct);
        await employee.StopAsync(ct);
        await employee.StartAsync(ct);
        await AssertNoMessageAsync(employeeEvents, ct);
    }

    [Fact]
    public async Task Shop_sync_updates_are_pushed_only_to_administrator_connections()
    {
        // Given 管理员与员工各一条已登录连接，管理员已收到初始快照。
        var ct = TestContext.Current.CancellationToken;
        var (adminLogin, employeeLogin) = await SeedAccountsAsync(ct);
        var job = new RecordingShopSyncJob();
        await using var app = await StartAppAsync(job, orderSync: false, ct);
        var adminEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        var employeeEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var admin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var adminSubscription = admin.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => adminEvents.Writer.TryWrite(status));
        using var employeeSubscription = employee.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => employeeEvents.Writer.TryWrite(status));
        await admin.StartAsync(ct);
        await employee.StartAsync(ct);
        await ReadAsync(adminEvents, ct);
        await AssertNoMessageAsync(employeeEvents, ct);

        // When 同步任务广播一次目录状态更新（管理员启动同步时走同一广播路径）。
        var update = new ShopSyncStatus(false, null, DateTimeOffset.UtcNow, 3, 3, 2, 1, 0, null, 7);
        await app.App.Services.GetRequiredService<IShopSyncNotifier>().PublishAsync(update, ct);

        // Then 管理员收到同轮事件，员工收不到。
        Assert.Equal(update, await ReadAsync(adminEvents, ct));
        await AssertNoMessageAsync(employeeEvents, ct);
    }

    [Fact]
    public async Task Shop_sync_updates_stop_for_disconnected_administrator_while_others_still_receive()
    {
        // Given 两个管理员连接和一个员工连接，均已收到各自的初始快照。
        var ct = TestContext.Current.CancellationToken;
        var (adminLogin, employeeLogin) = await SeedAccountsAsync(ct);
        var job = new RecordingShopSyncJob();
        await using var app = await StartAppAsync(job, orderSync: false, ct);
        var firstAdminEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        var secondAdminEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        var employeeEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var firstAdmin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var secondAdmin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var firstAdminSubscription = firstAdmin.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => firstAdminEvents.Writer.TryWrite(status));
        using var secondAdminSubscription = secondAdmin.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => secondAdminEvents.Writer.TryWrite(status));
        using var employeeSubscription = employee.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => employeeEvents.Writer.TryWrite(status));
        await firstAdmin.StartAsync(ct);
        await secondAdmin.StartAsync(ct);
        await employee.StartAsync(ct);
        await ReadAsync(firstAdminEvents, ct);
        await ReadAsync(secondAdminEvents, ct);
        await AssertNoMessageAsync(employeeEvents, ct);

        // When 第一个管理员断开后触发一次目录状态广播。
        await firstAdmin.StopAsync(ct);
        var update = new ShopSyncStatus(false, null, DateTimeOffset.UtcNow, 3, 3, 2, 1, 0, null, 11);
        await app.App.Services.GetRequiredService<IShopSyncNotifier>().PublishAsync(update, ct);

        // Then 仍在线的管理员收到，断开的管理员与员工都收不到，广播不抛异常。
        Assert.Equal(update, await ReadAsync(secondAdminEvents, ct));
        await AssertNoMessageAsync(firstAdminEvents, ct);
        await AssertNoMessageAsync(employeeEvents, ct);
    }

    [Fact]
    public async Task Real_shop_sync_started_via_hub_pushes_updates_to_administrator_only()
    {
        // Given 真实目录同步任务、真实通知器和 Hub 注册在同一宿主，上游由测试客户端替代。
        var ct = TestContext.Current.CancellationToken;
        var (adminLogin, employeeLogin) = await SeedAccountsAsync(ct);
        var erp = new TestErpClient
        {
            GetAllShops = (_, _, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([
                new("30", "店铺三十", "TemuPop", "US", "member-30")
            ])),
            GetPlatforms = (_, _) => Task.FromResult<(OpResult<IReadOnlyList<ErpPlatform>>, int)>((
                OpResults.Ok<IReadOnlyList<ErpPlatform>>([
                    new("TemuPop", "Temu半托管", "icon-temupop", true, 1)
                ]), 200)),
            GetShopStatus = (_, _, _) => Task.FromResult(OpResults.Ok(new ErpShopStatus(true, "US", true)))
        };
        await using var app = await StartRealAppAsync(erp, ct);
        var job = app.App.Services.GetRequiredService<IShopSyncJob>();
        var adminEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        var employeeEvents = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var admin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var adminSubscription = admin.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => adminEvents.Writer.TryWrite(status));
        using var employeeSubscription = employee.On<ShopSyncStatus>(
            ErpHub.ShopSyncUpdated, status => employeeEvents.Writer.TryWrite(status));
        await admin.StartAsync(ct);
        await employee.StartAsync(ct);
        Assert.False((await ReadAsync(adminEvents, ct)).Running);
        await AssertNoMessageAsync(employeeEvents, ct);

        // When 管理员经 Hub 启动一次真实目录同步。
        var started = await admin.InvokeAsync<JsonElement>("StartShopSync", ct);
        Assert.True(started.GetProperty("accepted").GetBoolean());

        // Then 管理员连接收到运行中的进度或终态，且最终完成；同轮员工连接收不到任何目录事件。
        ShopSyncStatus? final = null;
        while (final is null)
        {
            var status = await ReadAsync(adminEvents, ct);
            if (!status.Running)
                final = status;
        }
        await TestWait.UntilAsync(() => !job.Status.Running, "目录同步未在期限内结束。", ct);
        Assert.Null(final.Error);
        Assert.Equal(1, final.Total);
        Assert.Equal(1, final.Valid);
        await AssertNoMessageAsync(employeeEvents, ct);
    }

    [Fact]
    public async Task Start_shop_sync_rejects_employee_without_enqueuing()
    {
        // Given 管理员与员工各一条已登录连接，目录同步任务记录启动次数。
        var ct = TestContext.Current.CancellationToken;
        var (adminLogin, employeeLogin) = await SeedAccountsAsync(ct);
        var job = new RecordingShopSyncJob();
        await using var app = await StartAppAsync(job, orderSync: false, ct);
        await using var admin = CreateConnection(app.App, IssueToken(app.App, adminLogin));
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        await admin.StartAsync(ct);
        await employee.StartAsync(ct);

        // When 员工调用 StartShopSync。
        var rejected = await employee.InvokeAsync<JsonElement>("StartShopSync", ct);

        // Then 命令被拒绝且任务未入队。
        Assert.False(rejected.GetProperty("accepted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("status").ValueKind);
        Assert.Contains("仅管理员", rejected.GetProperty("error").GetString());
        Assert.Equal(0, job.StartCalls);

        // When 管理员调用 StartShopSync；Then 受理并入队一次。
        var accepted = await admin.InvokeAsync<JsonElement>("StartShopSync", ct);
        Assert.True(accepted.GetProperty("accepted").GetBoolean());
        Assert.Equal(JsonValueKind.Object, accepted.GetProperty("status").ValueKind);
        Assert.Equal(1, job.StartCalls);
    }

    [Fact]
    public async Task Employee_can_sync_and_receive_status_for_assigned_platform_shops_only()
    {
        // Given 员工只获分配其中一家平台店铺。
        var ct = TestContext.Current.CancellationToken;
        var (_, employeeLogin) = await SeedAccountsAsync(ct);
        var job = new RecordingShopSyncJob();
        await using var app = await StartAppAsync(job, orderSync: true, ct);
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignedShop = NewShop("已分配店铺");
        var otherShop = NewShop("未分配店铺");
        db.Shops.AddRange(assignedShop, otherShop);
        await db.SaveChangesAsync(ct);
        var (assignedSid, otherSid) = (assignedShop.Sid, otherShop.Sid);
        var assigned = new UserShopAssignment
        {
            UserId = employeeLogin.UserId, Platform = TestPlatformOrderSource.DefaultPlatform, Sid = assignedSid
        };
        db.UserShopAssignments.Add(assigned);
        await db.SaveChangesAsync(ct);
        var jobs = app.App.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([otherSid]).IsErr);
        await TestWait.UntilAsync(() => jobs.Latest(otherSid)?.Status == "succeeded", "定时同步未完成。", ct);

        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var snapshotSubscription = employee.On<OrderSyncSnapshot>(
            ErpHub.OrderSyncSnapshot, state => snapshots.Writer.TryWrite(state));
        using var updateSubscription = employee.On<OrderSyncStatus>(
            ErpHub.OrderSyncUpdated, state => updates.Writer.TryWrite(state));

        // When 员工连接并直接发出未分配店铺的编号；Then 快照和拒绝结果都不会泄露该店已有同步状态。
        await employee.StartAsync(ct);
        var snapshot = await ReadAsync(snapshots, ct);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Epoch));
        Assert.Empty(snapshot.Statuses);
        var rejected = await employee.InvokeAsync<OrderSyncStartResult>("StartOrderSync", otherSid, ct);
        Assert.False(rejected.Accepted);
        Assert.Null(rejected.Status);

        // When 未分配店铺先排队，员工发起自己的单店同步；Then 员工只收到已分配店铺的排队状态。
        await TestWait.EventuallyAsync(() => jobs.Start(otherSid), "未受理店铺补跑。", ct);
        var started = await employee.InvokeAsync<OrderSyncStartResult>("StartOrderSync", assignedSid, ct);
        Assert.True(started.Accepted, started.Error);
        var queued = await ReadAsync(updates, ct);
        Assert.Equal(assignedSid, queued.Sid);
        Assert.Equal("queued", queued.Stage);
        Assert.Equal(started.Status!.Id, queued.Id);
        OrderSyncStatus update;
        do
        {
            update = await ReadAsync(updates, ct);
            Assert.Equal(assignedSid, update.Sid);
            Assert.Equal(queued.Id, update.Id);
        } while (update.Status != "succeeded");

        // When 员工重连并调用一键同步；Then 快照仍只含自己店铺，受理计数也只涉及自己店铺。
        await employee.StopAsync(ct);
        await employee.StartAsync(ct);
        Assert.Equal(assignedSid, Assert.Single((await ReadAsync(snapshots, ct)).Statuses).Sid);
        var bulk = await employee.InvokeAsync<AllOrderSyncStartResult>("StartAllOrderSync", ct);
        Assert.True(bulk.Accepted, bulk.Error);
        Assert.Equal(1, bulk.StartedCount + bulk.BusyCount);

        // When 管理员撤销员工分配，员工再次重连；Then 先前可见的店铺状态也从快照消失。
        db.UserShopAssignments.Remove(assigned);
        await db.SaveChangesAsync(ct);
        await employee.StopAsync(ct);
        await employee.StartAsync(ct);
        Assert.Empty((await ReadAsync(snapshots, ct)).Statuses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Employee_receives_manual_sync_completion_for_shops_assigned_after_connecting(bool singleShop)
    {
        // Given 本地已有两家店，员工零分配连接并收到空快照。
        var ct = TestContext.Current.CancellationToken;
        var (_, employeeLogin) = await SeedAccountsAsync(ct);
        await using var app = await StartAppAsync(new RecordingShopSyncJob(), orderSync: true, ct);
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shopOne = NewShop("店铺 1");
        var shopTwo = NewShop("店铺 2");
        db.Shops.AddRange(shopOne, shopTwo);
        await db.SaveChangesAsync(ct);
        var (firstSid, secondSid) = (shopOne.Sid, shopTwo.Sid);

        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        var completed = Channel.CreateUnbounded<OrderSyncStatus>();
        await using var employee = CreateConnection(app.App, IssueToken(app.App, employeeLogin));
        using var snapshotSubscription = employee.On<OrderSyncSnapshot>(
            ErpHub.OrderSyncSnapshot, state => snapshots.Writer.TryWrite(state));
        using var updateSubscription = employee.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated, state =>
        {
            if (state.Status == "succeeded")
                completed.Writer.TryWrite(state);
        });
        await employee.StartAsync(ct);
        Assert.Empty((await ReadAsync(snapshots, ct)).Statuses);

        // When 分配店铺后，员工通过同一连接发起单店或一键同步。
        var sids = (singleShop ? new[] { firstSid } : new[] { firstSid, secondSid }).Order().ToArray();
        db.UserShopAssignments.AddRange(sids.Select(sid =>
            new UserShopAssignment { UserId = employeeLogin.UserId, Platform = TestPlatformOrderSource.DefaultPlatform, Sid = sid }));
        await db.SaveChangesAsync(ct);
        if (singleShop)
        {
            var start = await employee.InvokeAsync<OrderSyncStartResult>("StartOrderSync", firstSid, ct);
            Assert.True(start.Accepted, start.Error);
            Assert.Equal(firstSid, start.Status!.Sid);
        }
        else
        {
            var start = await employee.InvokeAsync<AllOrderSyncStartResult>("StartAllOrderSync", ct);
            Assert.True(start.Accepted, start.Error);
            Assert.Equal(sids.Length, start.StartedCount);
        }

        // Then 所有受理任务完成，原连接收到每家店的对应终态。
        var jobs = app.App.Services.GetRequiredService<OrderSyncJobs>();
        await TestWait.UntilAsync(() => sids.All(sid => jobs.Latest(sid)?.Status == "succeeded"),
            "新分配店铺的手动同步未完成。", ct);
        var received = new List<int>();
        foreach (var _ in sids)
        {
            var status = await ReadAsync(completed, ct);
            Assert.Equal(jobs.Latest(status.Sid)?.Id, status.Id);
            received.Add(status.Sid);
        }
        Assert.Equal(sids, received.Order().ToArray());
    }

    private async Task<(UserRefreshToken Admin, UserRefreshToken Employee)> SeedAccountsAsync(CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(_connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync(ct);

        var admin = new UserAccount { Username = UserAccount.AdminUsername, Name = "管理员", PasswordHash = "admin-hash" };
        var employee = new UserAccount { Username = "employee", Name = "员工", PasswordHash = "employee-hash" };
        db.Users.AddRange(admin, employee);
        await db.SaveChangesAsync(ct);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var adminLogin = new UserRefreshToken
        {
            UserId = admin.Id,
            User = admin,
            TokenHash = RandomNumberGenerator.GetBytes(32),
            ExpiresAt = expiresAt
        };
        var employeeLogin = new UserRefreshToken
        {
            UserId = employee.Id,
            User = employee,
            TokenHash = RandomNumberGenerator.GetBytes(32),
            ExpiresAt = expiresAt
        };
        db.RefreshTokens.AddRange(adminLogin, employeeLogin);
        await db.SaveChangesAsync(ct);
        return (adminLogin, employeeLogin);
    }

    private static Shop NewShop(string name) => TestShops.New(name);

    private async Task<TestApp> StartAppAsync(RecordingShopSyncJob job, bool orderSync, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(_connectionString));
        builder.AddAccountAuthentication();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IShopSyncJob>(job);
        builder.Services.AddSingleton<IShopSyncNotifier, ShopSyncNotifier>();
        if (orderSync)
        {
            builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
            builder.Services.AddSingleton<IPlatformShops>(new TestPlatformShops());
            builder.Services.AddSingleton(new PlatformOrderSources([new TestPlatformOrderSource()]));
            builder.Services.AddScoped<OrderStore>();
            builder.Services.AddSingleton<OrderSyncJobs>();
            builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
        }

        var app = builder.Build();
        app.MapHub<ErpHub>("/api/hubs/erp", options => options.CloseOnAuthenticationExpiration = true);
        try
        {
            await app.StartAsync(ct);
            return new TestApp(app);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private async Task<TestApp> StartRealAppAsync(TestErpClient erp, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(_connectionString));
        builder.AddAccountAuthentication();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IErpClient>(erp);
        builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IShopSyncNotifier, ShopSyncNotifier>();
        builder.Services.AddSingleton<ShopSyncJob>();
        builder.Services.AddSingleton<IShopSyncJob>(services => services.GetRequiredService<ShopSyncJob>());

        var app = builder.Build();
        app.MapHub<ErpHub>("/api/hubs/erp", options => options.CloseOnAuthenticationExpiration = true);
        try
        {
            await app.StartAsync(ct);
            return new TestApp(app);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static string IssueToken(WebApplication app, UserRefreshToken login) =>
        app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;

    private static HubConnection CreateConnection(WebApplication app, string accessToken) =>
        new HubConnectionBuilder().WithUrl(new Uri(new Uri(app.Urls.Single()), "/api/hubs/erp"), options =>
            options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken)).Build();

    private static Task<T> ReadAsync<T>(Channel<T> channel, CancellationToken ct) =>
        channel.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);

    private static async Task AssertNoMessageAsync<T>(Channel<T> channel, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.False(channel.Reader.TryRead(out _), "不应收到该事件。");
    }

    private sealed class RecordingShopSyncJob : IShopSyncJob
    {
        private int _startCalls;

        public int StartCalls => Volatile.Read(ref _startCalls);

        public ShopSyncStatus Status { get; } = new(false, null, null, 0, 0, 0, 0, 0, null, 0);

        public Task<OpResult<ShopSyncStatus>> TryStartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCalls);
            return Task.FromResult(OpResults.Ok(Status));
        }
    }

    private sealed class TestApp(WebApplication app) : IAsyncDisposable
    {
        public WebApplication App => app;

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
