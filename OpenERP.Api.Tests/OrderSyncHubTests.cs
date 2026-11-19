using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpResult;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncHubTests
{
    [Fact]
    public async Task Manual_sync_reports_configuration_and_token_failures_at_their_respective_stages()
    {
        // Given 已登录的管理员，独立测试库中存在一条无法解密的凭证。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await fixture.EnsureApiStartedAsync(ct);
        using var client = fixture.CreateApiClient();
        await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);
        await using var db = new NpgsqlConnection(await fixture.GetPostgresConnectionStringAsync(ct));
        await db.OpenAsync(ct);
        await using var seed = new NpgsqlCommand(
            """INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken") VALUES (1, 'invalid-protected-token')""", db);
        await seed.ExecuteNonQueryAsync(ct);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.ApiEndpoint, "/api/hubs/erp"), options =>
                options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization?.Parameter))
            .Build();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        using var subscription = connection.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated,
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);

        // When 通过 Hub 手动同步。Then 命令被受理，客户端随后收到同一任务的凭证失败终态。
        var start = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", 30, ct);
        Assert.True(start.Accepted, start.Error);
        var running = await ReadAsync(updates, ct);
        var failed = await ReadAsync(updates, ct);
        Assert.Equal("running", running.Status);
        Assert.Equal(start.Status!.Id, running.Id);
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
        }, ct);
        var shopUpdates = Channel.CreateUnbounded<ShopSyncStatus>();
        var updates = Channel.CreateUnbounded<JsonElement>();
        await using var connection = CreateConnection(app);
        using var shopSubscription = connection.On<ShopSyncStatus>(ErpHub.ShopSyncUpdated,
            state => shopUpdates.Writer.TryWrite(state));
        using var updateSubscription = connection.On<JsonElement>("OrderSyncUpdated",
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        await ReadAsync(shopUpdates, ct);

        // When 定时批次接受，逐步释放订单进度和最终结果。
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([30]).IsErr);
        var started = await ReadAsync(updates, ct);
        releaseProgress.SetResult();
        var progress = await ReadAsync(updates, ct);
        releaseResult.SetResult();
        var failed = await ReadAsync(updates, ct);

        // Then 同一订单任务的三个版本按顺序推送，失败保留最后读到的计数。
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
        Assert.True(started.GetProperty("version").GetInt64() < progress.GetProperty("version").GetInt64());
        Assert.True(progress.GetProperty("version").GetInt64() < failed.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Order_sync_command_aborts_a_connection_with_no_active_session()
    {
        // Given 一个已经建立连接、随后未能提供有效会话的调用方。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestErpClient(), ct);
        await using var connection = CreateConnection(app);
        await connection.StartAsync(ct);

        // When 调用方尝试手动发起订单同步。
        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync<JsonElement>("StartOrderSync", 30, ct));

        // Then 连接按会话失效规则关闭，且没有创建同步作业。
        await TestWait.UntilAsync(() => connection.State == HubConnectionState.Disconnected,
            "会话失效后 Hub 未断开连接。", ct);
        Assert.Null(app.Services.GetRequiredService<OrderSyncJobs>().Latest(30));
    }

    [Fact]
    public async Task Shop_hub_still_connects_when_order_sync_is_disabled()
    {
        // Given 环境未注册订单同步服务。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(orderSyncEnabled: false, new TestErpClient(), ct);
        var updates = Channel.CreateUnbounded<ShopSyncStatus>();
        await using var connection = CreateConnection(app);
        using var subscription = connection.On<ShopSyncStatus>(ErpHub.ShopSyncUpdated,
            state => updates.Writer.TryWrite(state));

        // When 连接 ERP Hub。
        await connection.StartAsync(ct);

        // Then 店铺同步的初始状态仍可正常接收。
        Assert.Equal(FakeShopSyncJob.Snapshot, await ReadAsync(updates, ct));
    }

    private static HubConnection CreateConnection(WebApplication app) =>
        new HubConnectionBuilder().WithUrl(new Uri(new Uri(app.Urls.Single()), "/api/hubs/erp")).Build();

    private static Task<T> ReadAsync<T>(Channel<T> channel, CancellationToken ct) =>
        channel.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);

    private static async Task<WebApplication> StartAppAsync(bool orderSyncEnabled, TestErpClient erp, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSignalR();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IShopSyncJob, FakeShopSyncJob>();
        if (orderSyncEnabled)
        {
            builder.Services.AddSingleton<IErpClient>(erp);
            builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
            builder.Services.AddSingleton<OrderSyncJobs>();
            builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
        }

        var app = builder.Build();
        app.MapHub<ErpHub>("/api/hubs/erp").AllowAnonymous();
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
