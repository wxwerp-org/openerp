using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpResult;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncHubTests
{
    [Fact]
    public async Task All_sync_covers_the_local_temu_directory_and_continues_after_disconnect()
    {
        // Given 超过一页 TemuPop 店铺，包含所有授权状态，另有一家其他平台店铺。
        var ct = TestContext.Current.CancellationToken;
        var sids = Enumerable.Range(30, 101).ToArray();
        var releaseOrders = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient
        {
            ReadOrders = async (sid, _, cancellationToken) =>
            {
                await releaseOrders.Task.WaitAsync(cancellationToken);
                return OpResults.Err($"店铺 {sid} 读取失败。");
            }
        }, ct, await fixture.GetPostgresConnectionStringAsync(ct));
        string accessToken;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(ct);
            var user = new UserAccount { Username = "batch-sync-user", PasswordHash = "test-hash" };
            var login = new UserRefreshToken
            {
                UserId = user.Id,
                User = user,
                TokenHash = new byte[32],
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };
            db.RefreshTokens.Add(login);
            db.Shops.AddRange(sids.Select(sid => new Shop
            {
                Sid = sid,
                Name = $"店铺 {sid}",
                Platform = "TemuPop",
                IsTokenOk = (sid % 3) switch { 0 => true, 1 => false, _ => null }
            }));
            db.Shops.Add(new Shop { Sid = 999, Name = "其他店铺", Platform = "Temu" });
            await db.SaveChangesAsync(ct);
            accessToken = app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;
        }
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        var busy = jobs.Start(30);
        if (busy.IsErr)
            throw new Xunit.Sdk.XunitException(busy.Error.Message);
        var busyId = busy.Value.Id;

        // When 员工一键同步，收到结果后断开连接，放行 ERP 读取。
        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        await using var connection = CreateConnection(app, accessToken);
        using var subscription = connection.On<OrderSyncSnapshot>(ErpHub.OrderSyncSnapshot,
            state => snapshots.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        await ReadAsync(snapshots, ct);
        var result = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        await connection.StopAsync(ct);
        releaseOrders.SetResult();

        // Then 批量任务跳过正在同步的店铺，其余 TemuPop 店铺全部执行并可在重连后恢复状态。
        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(100, result.GetProperty("startedCount").GetInt32());
        Assert.Equal(1, result.GetProperty("busyCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        await TestWait.UntilAsync(() => sids.All(sid => jobs.Latest(sid)?.Status == "failed"), "批量任务未全部结束。", ct);
        await connection.StartAsync(ct);
        var snapshot = await ReadAsync(snapshots, ct);
        Assert.Equal(sids, snapshot.Statuses.Select(status => status.Sid).Order().ToArray());
        Assert.Equal(busyId, Assert.Single(snapshot.Statuses, status => status.Sid == 30).Id);
        Assert.All(snapshot.Statuses, status => Assert.Equal($"店铺 {status.Sid} 读取失败。", status.Error));
    }

    [Fact]
    public async Task Manual_sync_reports_configuration_and_token_failures_at_their_respective_stages()
    {
        // Given 普通用户已登录，独立测试库尚未配置 ERP 凭证。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await fixture.EnsureApiStartedAsync(ct);
        using var administrator = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(administrator, ct, fixture.AdminPassword);
        const string username = "manual-sync-user";
        const string password = "manual-sync-password";
        using var created = await administrator.PostAsJsonAsync("/api/users",
            new { username, name = "订单同步员工", password }, ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var client = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(client, ct, password, username);
        await using var db = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await db.OpenAsync(ct);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.ApiEndpoint, "/api/hubs/erp"), options =>
                options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization?.Parameter))
            .Build();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        using var subscription = connection.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated,
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);

        // When 未配置凭证时一键同步。Then 命令被拒绝，未受理任何店铺。
        var unconfigured = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        AssertBulkRejection(unconfigured, "尚未配置私有 ERP AccessToken");

        await using var seed = new NpgsqlCommand("""
            INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken") VALUES (uuidv7(), 'invalid-protected-token');
            INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform") VALUES (uuidv7(), 30, '同步店铺', 'TemuPop');
            """, db);
        await seed.ExecuteNonQueryAsync(ct);

        // When 从普通用户入口读取同步可用性和可选店铺。Then 业务入口可用，系统配置返回 403。
        using var availability = await client.GetAsync("/api/erp/order-sync-availability", ct);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var capability = await availability.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(capability.GetProperty("configured").GetBoolean());
        Assert.True(capability.GetProperty("orderSyncEnabled").GetBoolean());
        using var shops = await client.GetAsync("/api/erp/shops?platform=TemuPop", ct);
        Assert.Equal(HttpStatusCode.OK, shops.StatusCode);
        var directory = await shops.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("30", Assert.Single(directory.GetProperty("items").EnumerateArray()).GetProperty("sid").GetString());
        using var configuration = await client.GetAsync("/api/erp/config", ct);
        Assert.Equal(HttpStatusCode.Forbidden, configuration.StatusCode);
        using var automaticSettings = await client.GetAsync("/api/erp/order-sync-settings", ct);
        Assert.Equal(HttpStatusCode.Forbidden, automaticSettings.StatusCode);

        // When 通过 Hub 手动同步。Then 命令被受理，客户端随后收到同一任务的凭证失败终态。
        var start = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", 30, ct);
        Assert.True(start.Accepted, start.Error);
        var queued = await ReadAsync(updates, ct);
        var running = await ReadAsync(updates, ct);
        var failed = await ReadAsync(updates, ct);
        Assert.Equal("queued", queued.Stage);
        Assert.Equal(start.Status!.Id, queued.Id);
        Assert.Equal(start.Status.Version, queued.Version);
        Assert.Equal("running", running.Status);
        Assert.Equal("shop", running.Stage);
        Assert.Equal(queued.Id, running.Id);
        Assert.True(running.Version > queued.Version);
        Assert.Equal(running.Id, failed.Id);
        Assert.Equal(running.Epoch, failed.Epoch);
        Assert.True(failed.Version > running.Version);
        Assert.Equal("shop", failed.Stage);
        Assert.Equal("failed", failed.Status);
        Assert.Equal("读取私有 ERP AccessToken 失败，请稍后重试。", failed.Error);

        // When 仅配置表不可读，会话仍有效。Then 再次启动被拒绝，并返回前次终态。
        await using var hideSettings = new NpgsqlCommand(
            """ALTER TABLE "ErpSettings" RENAME TO "UnavailableErpSettings" """, db);
        await hideSettings.ExecuteNonQueryAsync(ct);
        var rejected = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", 30, ct);
        Assert.False(rejected.Accepted);
        Assert.Equal("读取私有 ERP 配置失败，请稍后重试。", rejected.Error);
        Assert.Equal(failed.Id, rejected.Status!.Id);
        Assert.Equal(failed.Version, rejected.Status.Version);
        var allRejected = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        AssertBulkRejection(allRejected, "读取私有 ERP 配置失败，请稍后重试。");
    }

    [Fact]
    public async Task Reconnecting_receives_latest_status_for_each_shop_after_manual_and_scheduled_work()
    {
        // Given 两家店参加定时批次，随后为其中一家手动补跑。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient
        {
            ReadOrders = (sid, _, _) =>
                Task.FromResult<OpResult<OrderImportBatch>>(OpResults.Err($"店铺 {sid} 读取失败。"))
        }, ct);
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
        await TestWait.UntilAsync(() => jobs.Latest(31)?.Status == "failed", "定时批次未结束", ct);
        var manual = await TestWait.EventuallyAsync(() => jobs.Start(30), "手动补跑未受理", ct);
        await TestWait.UntilAsync(() => jobs.Latest(30)?.Status == "failed", "手动补跑未结束", ct);

        // When 连接时补齐快照，断线后使用同一客户端重新连接。
        var snapshot = Channel.CreateUnbounded<JsonElement>();
        await using var connection = CreateConnection(app);
        using var subscription = connection.On<JsonElement>("OrderSyncSnapshot",
            state => snapshot.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        var first = await ReadAsync(snapshot, ct);
        await connection.StopAsync(ct);
        await connection.StartAsync(ct);
        var second = await ReadAsync(snapshot, ct);

        // Then 两次都恢复各店最后结果，已补跑店铺使用新任务，版本排序保持一致。
        var statuses = second.GetProperty("statuses").EnumerateArray().ToDictionary(s => s.GetProperty("sid").GetInt32());
        Assert.Equal(2, statuses.Count);
        var epoch = second.GetProperty("epoch").GetString();
        Assert.False(string.IsNullOrWhiteSpace(epoch));
        Assert.Equal(epoch, first.GetProperty("epoch").GetString());
        Assert.Equal(manual.Id, statuses[30].GetProperty("id").GetString());
        Assert.Equal("店铺 30 读取失败。", statuses[30].GetProperty("error").GetString());
        Assert.Equal("failed", statuses[31].GetProperty("status").GetString());
        Assert.Equal(epoch, statuses[30].GetProperty("epoch").GetString());
        Assert.True(statuses[30].GetProperty("version").GetInt64() > statuses[31].GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Scheduled_batch_pushes_start_progress_and_failure_to_connected_clients()
    {
        // Given 客户端已连接，ERP 开始读取后可以按场景释放进度与失败结果。
        var ct = TestContext.Current.CancellationToken;
        var releaseProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient
        {
            ReadOrdersWithProgress = async (_, report, _, cancellationToken) =>
            {
                await releaseProgress.Task.WaitAsync(cancellationToken);
                report?.Invoke(new("details", 4, 4, 2));
                await releaseResult.Task.WaitAsync(cancellationToken);
                return OpResults.Err("ERP 读取中断。");
            }
        }, ct, hubRole: UserAccount.AdministratorRole);
        var shopUpdates = Channel.CreateUnbounded<ShopSyncStatus>();
        var updates = Channel.CreateUnbounded<JsonElement>();
        await using var connection = CreateConnection(app, role: UserAccount.AdministratorRole);
        using var shopSubscription = connection.On<ShopSyncStatus>(ErpHub.ShopSyncUpdated,
            state => shopUpdates.Writer.TryWrite(state));
        using var updateSubscription = connection.On<JsonElement>("OrderSyncUpdated",
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        await ReadAsync(shopUpdates, ct);

        // When 定时批次接受，逐步释放订单进度和最终结果。
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([30]).IsErr);
        var queued = await ReadAsync(updates, ct);
        var started = await ReadAsync(updates, ct);
        releaseProgress.SetResult();
        var progress = await ReadAsync(updates, ct);
        releaseResult.SetResult();
        var failed = await ReadAsync(updates, ct);

        // Then 同一订单任务的各个版本按顺序推送，失败保留最后读到的计数。
        Assert.Equal("queued", queued.GetProperty("stage").GetString());
        Assert.Equal("running", started.GetProperty("status").GetString());
        Assert.Equal("shop", started.GetProperty("stage").GetString());
        Assert.Equal(30, started.GetProperty("sid").GetInt32());
        Assert.Equal("details", progress.GetProperty("stage").GetString());
        Assert.Equal(4, progress.GetProperty("total").GetInt32());
        Assert.Equal(4, progress.GetProperty("read").GetInt32());
        Assert.Equal(2, progress.GetProperty("processed").GetInt32());
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal("ERP 读取中断。", failed.GetProperty("error").GetString());
        Assert.Equal(2, failed.GetProperty("processed").GetInt32());
        Assert.Equal(started.GetProperty("id").GetString(), failed.GetProperty("id").GetString());
        Assert.True(queued.GetProperty("version").GetInt64() < started.GetProperty("version").GetInt64());
        Assert.True(started.GetProperty("version").GetInt64() < progress.GetProperty("version").GetInt64());
        Assert.True(progress.GetProperty("version").GetInt64() < failed.GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connected_client_receives_queued_shop_before_a_worker_is_free(bool scheduled)
    {
        // Given 已连接的客户端，四家店将占住全部执行位置。
        var ct = TestContext.Current.CancellationToken;
        var fourShopsReading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient
        {
            ReadOrders = async (_, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref reads) == 4)
                    fourShopsReading.SetResult();

                await releaseReads.Task.WaitAsync(cancellationToken);
                return OpResults.Err("ERP 读取中断。");
            }
        }, ct);
        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        await using var connection = CreateConnection(app);
        using var snapshotSubscription = connection.On<OrderSyncSnapshot>(ErpHub.OrderSyncSnapshot,
            state => snapshots.Writer.TryWrite(state));
        using var updateSubscription = connection.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated,
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        await ReadAsync(snapshots, ct);

        // When 一次受理五家店，前四家进入读取。
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        var sids = new[] { 30, 31, 32, 33, 34 };
        if (scheduled)
            Assert.False(jobs.StartScheduledBatch(sids).IsErr);
        else
            Assert.False(jobs.StartAll(sids).IsErr);
        await fourShopsReading.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // Then 客户端在第五家实际执行前收到排队状态，释放位置后同一任务进入读取。
        OrderSyncStatus queued;
        do
        {
            queued = await ReadAsync(updates, ct);
        } while (queued.Sid != 34);
        Assert.Equal("running", queued.Status);
        Assert.Equal("queued", queued.Stage);
        Assert.Equal(4, Volatile.Read(ref reads));

        releaseReads.SetResult();
        OrderSyncStatus started;
        do
        {
            started = await ReadAsync(updates, ct);
        } while (started.Sid != 34);
        Assert.Equal("running", started.Status);
        Assert.Equal("shop", started.Stage);
        Assert.Equal(queued.Id, started.Id);
        Assert.True(started.Version > queued.Version);
    }

    [Theory]
    [InlineData("StartOrderSync", true)]
    [InlineData("StartAllOrderSync", false)]
    public async Task Order_sync_command_aborts_a_connection_with_no_active_session(string command, bool hasSid)
    {
        // Given 一个已经建立连接、随后未能提供有效会话的调用方。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient(), ct);
        await using var connection = CreateConnection(app);
        await connection.StartAsync(ct);

        // When 调用方尝试手动发起订单同步。
        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeCoreAsync<JsonElement>(command, hasSid ? [30] : [], ct));

        // Then 连接按会话失效规则关闭，且没有创建同步作业。
        await TestWait.UntilAsync(() => connection.State == HubConnectionState.Disconnected,
            "会话失效后 Hub 未断开连接。", ct);
        Assert.Empty(app.Services.GetRequiredService<OrderSyncJobs>().Snapshot().Statuses);
    }

    [Fact]
    public async Task Shop_hub_still_connects_when_order_sync_is_disabled()
    {
        // Given 环境未注册订单同步服务。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(
            orderSyncEnabled: false, new TestErpClient(), ct, hubRole: UserAccount.AdministratorRole);
        var updates = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var connection = CreateConnection(app, role: UserAccount.AdministratorRole);
        using var subscription = connection.On<ShopSyncStatus>(ErpHub.ShopSyncUpdated,
            state => updates.Writer.TryWrite(state));

        // When 连接 ERP Hub。
        await connection.StartAsync(ct);

        // Then 店铺同步的初始状态仍可正常接收。
        Assert.Equal(FakeShopSyncJob.Snapshot, await ReadAsync(updates, ct));
    }

    private static HubConnection CreateConnection(WebApplication app, string? accessToken = null, string? role = null) =>
        new HubConnectionBuilder().WithUrl(new Uri(new Uri(app.Urls.Single()), "/api/hubs/erp"), options =>
        {
            options.AccessTokenProvider = () => Task.FromResult(accessToken);
            if (role is not null)
                options.Headers.Add(ErpEndpointTestAuth.RoleHeader, role);
        }).Build();

    private static Task<T> ReadAsync<T>(Channel<T> channel, CancellationToken ct) =>
        channel.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);

    private static void AssertBulkRejection(JsonElement result, string error)
    {
        Assert.False(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(0, result.GetProperty("startedCount").GetInt32());
        Assert.Equal(0, result.GetProperty("busyCount").GetInt32());
        Assert.Equal(error, result.GetProperty("error").GetString());
    }

    private static async Task<WebApplication> StartAppAsync(
        bool orderSyncEnabled, TestErpClient erp, CancellationToken ct, string? connectionString = null,
        string? hubRole = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSignalR();
        if (connectionString is not null)
        {
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            builder.AddAccountAuthentication();
        }
        else if (hubRole is not null)
        {
            ErpEndpointTestAuth.Configure(builder);
        }
        else
        {
            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();
        }
        builder.Services.AddSingleton<IShopSyncJob, FakeShopSyncJob>();
        if (orderSyncEnabled)
        {
            builder.Services.AddSingleton<IErpClient>(erp);
            builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
            builder.Services.AddSingleton<OrderSyncJobs>();
            builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
        }

        var app = builder.Build();
        var hub = app.MapHub<ErpHub>("/api/hubs/erp");
        if (connectionString is null && hubRole is null)
            hub.AllowAnonymous();
        try
        {
            await app.StartAsync(ct);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class FakeShopSyncJob : IShopSyncJob
    {
        internal static ShopSyncStatus Snapshot { get; } = new(false, null, null, 0, 0, 0, 0, 0, null, 0);

        public ShopSyncStatus Status => Snapshot;

        public Task<OpResult<ShopSyncStatus>> TryStartAsync(CancellationToken cancellationToken) =>
            Task.FromResult<OpResult<ShopSyncStatus>>(OpResults.Err("测试中不触发店铺同步。"));
    }
}
