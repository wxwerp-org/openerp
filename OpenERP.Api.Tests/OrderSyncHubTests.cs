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
using OpenERP.Api.Orders.Sources;
using OpResult;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncHubTests
{
    [Theory]
    [InlineData("StartOrderSync", true)]
    [InlineData("StartAllOrderSync", false)]
    public async Task Employee_without_assignments_cannot_start_order_sync(string command, bool hasSid)
    {
        // Given 已录入一家平台店铺，新员工尚未获分配。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(), ct,
            await fixture.GetPostgresConnectionStringAsync(ct));
        var accessToken = await SeedSyncEmployeeAsync(app, ct);
        await using var connection = CreateConnection(app, accessToken);
        await connection.StartAsync(ct);

        // When 员工直接请求同步该 SID 或一键同步。
        var result = await connection.InvokeCoreAsync<JsonElement>(command, hasSid ? [30] : [], ct);

        // Then 单店越权返回拒绝，一键同步受理零家，均不创建订单同步任务。
        if (hasSid)
        {
            Assert.False(result.GetProperty("accepted").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("status").ValueKind);
            Assert.Equal("无权同步该店铺。", result.GetProperty("error").GetString());
        }
        else
        {
            Assert.True(result.GetProperty("accepted").GetBoolean());
            Assert.Equal(0, result.GetProperty("startedCount").GetInt32());
            Assert.Equal(0, result.GetProperty("busyCount").GetInt32());
        }
        Assert.Empty(app.Services.GetRequiredService<OrderSyncJobs>().Snapshot().Statuses);
    }

    [Fact]
    public async Task Employee_without_assignments_receives_no_order_sync_statuses()
    {
        // Given 一个已完成的同步任务，新员工尚未获分配。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(
            (_, _, _) => Task.FromResult<OpResult<OrderImportBatch>>(OpResults.Err("平台读取中断。"))), ct, await fixture.GetPostgresConnectionStringAsync(ct));
        var accessToken = await SeedSyncEmployeeAsync(app, ct);
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([30]).IsErr);
        await TestWait.UntilAsync(() => jobs.Latest(30)?.Status == "failed", "定时批次未结束。", ct);

        // When 该员工连接、重连，后台再次同步同一家店。
        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        await using var connection = CreateConnection(app, accessToken);
        using var snapshotSubscription = connection.On<OrderSyncSnapshot>(ErpHub.OrderSyncSnapshot,
            state => snapshots.Writer.TryWrite(state));
        using var updateSubscription = connection.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated,
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        var first = await ReadAsync(snapshots, ct);
        await connection.StopAsync(ct);
        await connection.StartAsync(ct);
        var second = await ReadAsync(snapshots, ct);
        var repeat = await TestWait.EventuallyAsync(() => jobs.Start(30), "补跑未受理。", ct);
        await TestWait.UntilAsync(() => jobs.Latest(30) is { Status: "failed" } status && status.Id == repeat.Id,
            "补跑未结束。", ct);
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        observation.CancelAfter(TimeSpan.FromMilliseconds(500));
        var delivered = false;
        try
        {
            delivered = await updates.Reader.WaitToReadAsync(observation.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 观察期结束，员工没有收到增量。
        }

        // Then 两次快照为空，员工也未收到同一家店的增量。
        Assert.Multiple(
            () => Assert.Empty(first.Statuses),
            () => Assert.Empty(second.Statuses),
            () => Assert.False(delivered));
    }

    [Fact]
    public async Task Administrator_all_sync_covers_every_platform_shop_and_continues_after_disconnect()
    {
        // Given 超过一页的直连平台店铺。
        var ct = TestContext.Current.CancellationToken;
        const int shopCount = 101;
        var releaseOrders = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(async (shop, _, cancellationToken) =>
        {
            await releaseOrders.Task.WaitAsync(cancellationToken);
            return OpResults.Err($"店铺 {shop.Sid} 读取失败。");
        }), ct, await fixture.GetPostgresConnectionStringAsync(ct));
        string accessToken;
        int[] sids;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(ct);
            var user = new UserAccount { Username = UserAccount.AdminUsername, PasswordHash = "test-hash" };
            var login = new UserRefreshToken
            {
                UserId = user.Id,
                User = user,
                TokenHash = new byte[32],
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };
            db.RefreshTokens.Add(login);
            var shops = Enumerable.Range(0, shopCount).Select(index => NewShop(index)).ToArray();
            db.Shops.AddRange(shops);
            await db.SaveChangesAsync(ct);
            sids = shops.Select(shop => shop.Sid).Order().ToArray();
            accessToken = app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;
        }
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        var busy = jobs.Start(sids[0]);
        if (busy.IsErr)
            throw new Xunit.Sdk.XunitException(busy.Error.Message);
        var busyId = busy.Value.Id;

        // When 管理员一键同步，收到结果后断开连接，放行平台读取。
        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        await using var connection = CreateConnection(app, accessToken);
        using var subscription = connection.On<OrderSyncSnapshot>(ErpHub.OrderSyncSnapshot,
            state => snapshots.Writer.TryWrite(state));
        await connection.StartAsync(ct);
        await ReadAsync(snapshots, ct);
        var result = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        await connection.StopAsync(ct);
        releaseOrders.SetResult();

        // Then 批量任务跳过正在同步的店铺，其余店铺全部执行并可在重连后恢复状态。
        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(100, result.GetProperty("startedCount").GetInt32());
        Assert.Equal(1, result.GetProperty("busyCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        await TestWait.UntilAsync(() => sids.All(sid => jobs.Latest(sid)?.Status == "failed"), "批量任务未全部结束。", ct);
        await connection.StartAsync(ct);
        var snapshot = await ReadAsync(snapshots, ct);
        Assert.Equal(sids, snapshot.Statuses.Select(status => status.Sid).Order().ToArray());
        Assert.Equal(busyId, Assert.Single(snapshot.Statuses, status => status.Sid == sids[0]).Id);
        Assert.All(snapshot.Statuses, status => Assert.Equal($"店铺 {status.Sid} 读取失败。", status.Error));
    }

    [Fact]
    public async Task Sync_all_ignores_shops_of_platforms_without_an_order_source_and_single_sync_rejects_them()
    {
        // Given 授权店铺里有 3 家已实现订单同步的速卖通店铺和 2 家平台未实现的 TemuPop 店铺。
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true,
            new TestPlatformOrderSource((shop, _, _) => Task.FromResult<OpResult<OrderImportBatch>>(OpResults.Err($"店铺 {shop.Sid} 读取失败。"))),
            ct, await fixture.GetPostgresConnectionStringAsync(ct));
        string accessToken;
        int[] supported;
        int[] unsupported;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(ct);
            var user = new UserAccount { Username = UserAccount.AdminUsername, PasswordHash = "test-hash" };
            var login = new UserRefreshToken
            {
                UserId = user.Id,
                User = user,
                TokenHash = new byte[32],
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };
            db.RefreshTokens.Add(login);
            var good = Enumerable.Range(0, 3).Select(index => TestShops.New($"速卖通 {index}")).ToArray();
            var bad = Enumerable.Range(0, 2).Select(index => TestShops.New($"暂不支持 {index}", "TemuPop")).ToArray();
            db.Shops.AddRange(good);
            db.Shops.AddRange(bad);
            await db.SaveChangesAsync(ct);
            supported = good.Select(shop => shop.Sid).Order().ToArray();
            unsupported = bad.Select(shop => shop.Sid).Order().ToArray();
            accessToken = app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;
        }

        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        await using var connection = CreateConnection(app, accessToken);
        await connection.StartAsync(ct);

        // When 管理员一键同步。
        var all = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);

        // Then 只受理 3 家受支持的店铺，未实现平台的店铺没有任务。
        Assert.True(all.GetProperty("accepted").GetBoolean());
        Assert.Equal(3, all.GetProperty("startedCount").GetInt32());
        Assert.Equal(0, all.GetProperty("busyCount").GetInt32());
        await TestWait.UntilAsync(() => supported.All(sid => jobs.Latest(sid)?.Status == "failed"), "受支持店铺的同步未结束。", ct);
        Assert.All(unsupported, sid => Assert.Null(jobs.Latest(sid)));

        // When 单独同步未实现平台的店铺或不在目录里的店铺。
        var noSource = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", unsupported[0], ct);
        var missing = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", 999_999, ct);

        // Then 都被拒绝并给出原因，不产生任务。
        Assert.False(noSource.Accepted);
        Assert.Equal("TemuPop 平台暂未实现订单同步。", noSource.Error);
        Assert.False(missing.Accepted);
        Assert.Equal("店铺不存在或已从授权店铺中移除。", missing.Error);
        Assert.Null(jobs.Latest(unsupported[0]));
    }

    [Fact]
    public async Task Order_sync_commands_are_rejected_before_any_work_when_the_erp_token_is_not_configured()
    {
        // Given 授权店铺里有一家速卖通店铺，管理员已登录，但 ERP Token 尚未配置。
        var ct = TestContext.Current.CancellationToken;
        var readCalled = false;
        await using var fixture = new AspireIntegrationFixture();
        await fixture.InitializeAsync();
        await using var app = await StartAppAsync(orderSyncEnabled: true,
            new TestPlatformOrderSource((_, _, _) =>
            {
                readCalled = true;
                return Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));
            }),
            ct, await fixture.GetPostgresConnectionStringAsync(ct), erpToken: "");
        string accessToken;
        int sid;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(ct);
            var user = new UserAccount { Username = UserAccount.AdminUsername, PasswordHash = "test-hash" };
            var login = new UserRefreshToken
            {
                UserId = user.Id,
                User = user,
                TokenHash = new byte[32],
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };
            db.RefreshTokens.Add(login);
            var shop = TestShops.New("速卖通店铺");
            db.Shops.Add(shop);
            await db.SaveChangesAsync(ct);
            sid = shop.Sid;
            accessToken = app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;
        }

        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        await using var connection = CreateConnection(app, accessToken);
        await connection.StartAsync(ct);

        // When 单店同步和全部同步。
        var single = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", sid, ct);
        var all = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);

        // Then 两个命令都被拒绝并提示先配置 ERP，没有任务，也没有读取订单。
        Assert.False(single.Accepted);
        Assert.Equal("尚未配置私有 ERP AccessToken", single.Error);
        AssertBulkRejection(all, "尚未配置私有 ERP AccessToken");
        Assert.Null(jobs.Latest(sid));
        Assert.False(readCalled);
    }

    [Fact]
    public async Task Manual_sync_needs_the_erp_token_and_database_outages_are_rejections()
    {
        // Given 普通用户已登录，被分配了一家速卖通店铺，ERP Token 尚未配置。
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
        const int sid = 4242;
        await using var seedShop = new NpgsqlCommand("""
            INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform")
                VALUES (uuidv7(), 4242, '同步店铺', 'AliExpress');
            """, db);
        await seedShop.ExecuteNonQueryAsync(ct);
        await using var assign = new NpgsqlCommand("""
            INSERT INTO "UserShopAssignments" ("UserId", "Platform", "Sid")
                SELECT "Id", 'AliExpress', @sid FROM "Users" WHERE "Username" = @username;
            """, db);
        assign.Parameters.AddWithValue("sid", sid);
        assign.Parameters.AddWithValue("username", username);
        await assign.ExecuteNonQueryAsync(ct);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.ApiEndpoint, "/api/hubs/erp"), options =>
                options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization?.Parameter))
            .Build();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        using var subscription = connection.On<OrderSyncStatus>(ErpHub.OrderSyncUpdated,
            state => updates.Writer.TryWrite(state));
        await connection.StartAsync(ct);

        // When 从普通用户入口读取同步可用性和可见店铺。Then 业务入口可用，系统配置与定时设置返回 403。
        using var availability = await client.GetAsync("/api/order-sync/availability", ct);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var capability = await availability.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(capability.GetProperty("orderSyncEnabled").GetBoolean());
        Assert.Contains("AliExpress", capability.GetProperty("platforms").EnumerateArray().Select(item => item.GetString()));
        using var shops = await client.GetAsync("/api/order-sync/shops", ct);
        Assert.Equal(HttpStatusCode.OK, shops.StatusCode);
        var directory = await shops.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(sid, Assert.Single(directory.GetProperty("items").EnumerateArray()).GetProperty("sid").GetInt32());
        using var configuration = await client.GetAsync("/api/erp/config", ct);
        Assert.Equal(HttpStatusCode.Forbidden, configuration.StatusCode);
        using var automaticSettings = await client.GetAsync("/api/order-sync/settings", ct);
        Assert.Equal(HttpStatusCode.Forbidden, automaticSettings.StatusCode);

        // When 通过 Hub 手动同步。Then 命令被拒绝并提示先配置 ERP，没有创建任务。
        var start = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", sid, ct);
        Assert.False(start.Accepted);
        Assert.Equal("尚未配置私有 ERP AccessToken", start.Error);
        var all = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        AssertBulkRejection(all, "尚未配置私有 ERP AccessToken");

        // When 管理员配置 ERP Token 后仅店铺表不可读，会话仍有效。Then 再次启动被拒绝。
        using var saveToken = await administrator.PutAsJsonAsync("/api/erp/config", new { accessToken = "test-erp-token" }, ct);
        Assert.Equal(HttpStatusCode.NoContent, saveToken.StatusCode);
        await using var hideShops = new NpgsqlCommand(
            """ALTER TABLE "Shops" RENAME TO "UnavailableShops" """, db);
        await hideShops.ExecuteNonQueryAsync(ct);
        var rejected = await connection.InvokeAsync<OrderSyncStartResult>("StartOrderSync", sid, ct);
        Assert.False(rejected.Accepted);
        Assert.Equal("本地店铺数据暂时不可用。", rejected.Error);
        var allRejected = await connection.InvokeAsync<JsonElement>("StartAllOrderSync", ct);
        AssertBulkRejection(allRejected, "本地店铺数据暂时不可用。");
    }

    [Fact]
    public async Task Reconnecting_receives_latest_status_for_each_shop_after_manual_and_scheduled_work()
    {
        // Given 两家店参加定时批次，随后为其中一家手动补跑。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource((shop, _, _) =>
            Task.FromResult<OpResult<OrderImportBatch>>(OpResults.Err($"店铺 {shop.Sid} 读取失败。"))), ct);
        var jobs = app.Services.GetRequiredService<OrderSyncJobs>();
        Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
        await TestWait.UntilAsync(() => jobs.Latest(31)?.Status == "failed", "定时批次未结束", ct);
        var manual = await TestWait.EventuallyAsync(() => jobs.Start(30), "手动补跑未受理", ct);
        await TestWait.UntilAsync(() => jobs.Latest(30)?.Status == "failed", "手动补跑未结束", ct);

        // When 连接时补齐快照，断线后使用同一客户端重新连接。
        var snapshot = Channel.CreateUnbounded<JsonElement>();
        await using var connection = CreateConnection(app, role: UserAccount.AdministratorRole);
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
        // Given 客户端已连接，平台开始读取后可以按场景释放进度与失败结果。
        var ct = TestContext.Current.CancellationToken;
        var releaseProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(async (_, report, cancellationToken) =>
        {
            await releaseProgress.Task.WaitAsync(cancellationToken);
            report?.Invoke(new("details", 4, 4, 2));
            await releaseResult.Task.WaitAsync(cancellationToken);
            return OpResults.Err("平台读取中断。");
        }), ct);
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
        Assert.Equal("平台读取中断。", failed.GetProperty("error").GetString());
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
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(async (_, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref reads) == 4)
                fourShopsReading.SetResult();

            await releaseReads.Task.WaitAsync(cancellationToken);
            return OpResults.Err("平台读取中断。");
        }), ct);
        var snapshots = Channel.CreateUnbounded<OrderSyncSnapshot>();
        var updates = Channel.CreateUnbounded<OrderSyncStatus>();
        await using var connection = CreateConnection(app, role: UserAccount.AdministratorRole);
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
        await using var app = await StartAppAsync(orderSyncEnabled: true, new TestPlatformOrderSource(), ct);
        await using var connection = CreateConnection(app, role: UserAccount.AdministratorRole);
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
        await using var app = await StartAppAsync(orderSyncEnabled: false, new TestPlatformOrderSource(), ct);
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

    private static async Task<string> SeedSyncEmployeeAsync(WebApplication app, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);
        var user = new UserAccount { Username = "sync-employee", PasswordHash = "test-hash" };
        var login = new UserRefreshToken
        {
            User = user,
            UserId = user.Id,
            TokenHash = new byte[32],
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.RefreshTokens.Add(login);
        db.Shops.Add(NewShop(0));
        await db.SaveChangesAsync(ct);
        return app.Services.GetRequiredService<AccessTokens>().Issue(login, DateTimeOffset.UtcNow)!.Value.Value;
    }

    private static Shop NewShop(int index) => TestShops.New($"店铺 {index}");

    private static void AssertBulkRejection(JsonElement result, string error)
    {
        Assert.False(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(0, result.GetProperty("startedCount").GetInt32());
        Assert.Equal(0, result.GetProperty("busyCount").GetInt32());
        Assert.Equal(error, result.GetProperty("error").GetString());
    }

    private static async Task<WebApplication> StartAppAsync(
        bool orderSyncEnabled, TestPlatformOrderSource source, CancellationToken ct, string? connectionString = null,
        string erpToken = "test-token")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSignalR();
        if (connectionString is not null)
        {
            builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(connectionString));
            builder.AddAccountAuthentication();
        }
        else
        {
            ErpEndpointTestAuth.Configure(builder);
        }
        builder.Services.AddSingleton<IShopSyncJob, FakeShopSyncJob>();
        if (orderSyncEnabled)
        {
            builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore(erpToken));
            builder.Services.AddSingleton<IPlatformShops>(new TestPlatformShops());
            builder.Services.AddSingleton(new PlatformOrderSources([source]));
            builder.Services.AddSingleton<OrderSyncJobs>();
            builder.Services.AddHostedService(services => services.GetRequiredService<OrderSyncJobs>());
        }

        var app = builder.Build();
        app.MapHub<ErpHub>("/api/hubs/erp");
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
