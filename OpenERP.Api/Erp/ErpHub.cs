using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;

namespace OpenERP.Api.Erp;

/// <summary>ERP 长时交互 Hub；承载店铺与订单同步。</summary>
[Authorize]
public sealed class ErpHub(
    IShopSyncJob job,
    IServiceScopeFactory scopeFactory,
    OrderSyncJobs? orders = null) : Hub
{
    public const string ShopSyncUpdated = "ShopSyncUpdated";
    public const string OrderSyncUpdated = "OrderSyncUpdated";
    public const string OrderSyncSnapshot = "OrderSyncSnapshot";

    /// <summary>管理员连接的分发组；组员由 SignalR 随连接建立和结束自动维护。</summary>
    internal const string AdministratorGroup = "openerp.administrators";

    internal static string OrderSyncGroup(int sid) => $"openerp.order-sync.{sid}";

    public override async Task OnConnectedAsync()
    {
        // 鉴权通过后加入管理员组：多实例下由 backplane 分发，连接结束由 SignalR 自动移除，
        // 不会因快照发送失败留下无人清理的连接 ID。
        if (IsAdministrator())
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId, AdministratorGroup, Context.ConnectionAborted);

            ShopSyncStatus status;
            try
            {
                status = await ReadStatusAsync(Context.ConnectionAborted);
            }
            catch
            {
                status = job.Status;
            }

            await Clients.Caller.SendAsync(ShopSyncUpdated, status, Context.ConnectionAborted);
        }

        if (orders is not null)
        {
            var snapshot = await SubscribeAndReadOrderSnapshotAsync(orders, Context.ConnectionAborted);
            await Clients.Caller.SendAsync(OrderSyncSnapshot, snapshot, Context.ConnectionAborted);
        }
        await base.OnConnectedAsync();
    }

    private async Task<OrderSyncSnapshot> SubscribeAndReadOrderSnapshotAsync(
        OrderSyncJobs jobs, CancellationToken cancellationToken)
    {
        if (IsAdministrator())
            return jobs.Snapshot();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sids = await ShopAccess.For(Context.User!).Assignments(db)
            .Select(assignment => assignment.Sid)
            .ToHashSetAsync(cancellationToken);

        // 先订阅再读取快照，由事件版本处理加入订阅期间的并发更新。
        // 连接重新建立时重新读取分配，SignalR 随连接结束移除组成员。
        foreach (var sid in sids)
            await SubscribeToOrderSyncAsync(sid, cancellationToken);

        var snapshot = jobs.Snapshot();
        return snapshot with { Statuses = snapshot.Statuses.Where(status => sids.Contains(status.Sid)).ToArray() };
    }

    public async Task<ShopSyncStartResult> StartShopSync()
    {
        await EnsureActiveSessionAsync();
        if (!IsAdministrator())
            return new(false, null, "仅管理员可同步店铺目录。");

        var result = await job.TryStartAsync(Context.ConnectionAborted);
        if (result.IsErr)
            return new ShopSyncStartResult(false, await ReadStatusAsync(Context.ConnectionAborted), result.Error.Message);

        return new ShopSyncStartResult(true, result.Value, null);
    }

    public async Task<OrderSyncStartResult> StartOrderSync(int sid)
    {
        await EnsureActiveSessionAsync();
        if (orders is null)
            return new(false, null, "当前环境未启用订单同步。");
        if (sid <= 0)
            return new(false, null, "店铺 SID 必须为正整数");

        var ct = Context.ConnectionAborted;
        await using var scope = scopeFactory.CreateAsyncScope();
        var access = ShopAccess.For(Context.User!);
        if (!access.IsAdministrator)
        {
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await access.AllowsAsync(db, null, sid, ct))
                    return new(false, null, "无权同步该店铺。");
            }
            catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
            {
                return new(false, null, "本地店铺数据暂时不可用。");
            }
        }

        try
        {
            var shopDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var platform = await shopDb.Shops.AsNoTracking().Where(shop => shop.Sid == sid)
                .Select(shop => shop.Platform).FirstOrDefaultAsync(ct);
            if (platform is null)
                return new(false, orders.Latest(sid), "店铺不存在或已从授权店铺中移除。");
            if (scope.ServiceProvider.GetRequiredService<PlatformOrderSources>().Find(platform) is null)
                return new(false, orders.Latest(sid), $"{platform} 平台暂未实现订单同步。");
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return new(false, null, "本地店铺数据暂时不可用。");
        }

        var tokens = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
        var configuration = await tokens.GetConfigurationAsync(ct);
        if (configuration.IsErr)
            return new(false, orders.Latest(sid), configuration.Error.Message);
        if (!configuration.Value.Configured)
            return new(false, orders.Latest(sid), "尚未配置私有 ERP AccessToken");

        // 分配可能在连接建立后新增，先订阅再受理，以免漏掉排队后的进度。
        if (!access.IsAdministrator)
            await SubscribeToOrderSyncAsync(sid, ct);

        var started = orders.Start(sid);
        return started.IsErr
            ? new(false, orders.Latest(sid), started.Error.Message)
            : new(true, started.Value, null);
    }

    public async Task<AllOrderSyncStartResult> StartAllOrderSync()
    {
        await EnsureActiveSessionAsync();
        if (orders is null)
            return new(false, 0, 0, "当前环境未启用订单同步。");

        var ct = Context.ConnectionAborted;
        await using var scope = scopeFactory.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
        var configuration = await tokens.GetConfigurationAsync(ct);
        if (configuration.IsErr)
            return new(false, 0, 0, configuration.Error.Message);
        if (!configuration.Value.Configured)
            return new(false, 0, 0, "尚未配置私有 ERP AccessToken");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var access = ShopAccess.For(Context.User!);
        int[] sids;
        try
        {
            // 尚未实现订单同步的平台不参与全部同步。
            var platforms = scope.ServiceProvider.GetRequiredService<PlatformOrderSources>().Platforms.ToArray();
            sids = await access.Shops(db)
                .Where(shop => platforms.Contains(shop.Platform))
                .Select(shop => shop.Sid)
                .ToArrayAsync(ct);
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return new(false, 0, 0, "本地店铺数据暂时不可用。");
        }

        if (!access.IsAdministrator)
        {
            foreach (var sid in sids)
                await SubscribeToOrderSyncAsync(sid, ct);
        }

        var started = orders.StartAll(sids);
        if (started.IsErr)
            return new(false, 0, 0, started.Error.Message);

        return new(true, started.Value.StartedCount, started.Value.BusyCount, null);
    }

    private Task SubscribeToOrderSyncAsync(int sid, CancellationToken cancellationToken) =>
        Groups.AddToGroupAsync(Context.ConnectionId, OrderSyncGroup(sid), cancellationToken);

    private bool IsAdministrator() =>
        Context.User?.IsInRole(UserAccount.AdministratorRole) == true;

    private async Task EnsureActiveSessionAsync()
    {
        // 连接建立后，命令执行前仍须按当前数据校验会话。
        if (!await IsSessionActiveAsync(Context.ConnectionAborted))
        {
            Context.Abort();
            throw new HubException("登录会话已失效，请重新登录。");
        }
    }

    private async Task<ShopSyncStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ShopSyncStatusQueries.ReadAsync(job, db, cancellationToken);
    }

    private async Task<bool> IsSessionActiveAsync(CancellationToken cancellationToken)
    {
        var id = Context.User?.FindFirstValue(AccessTokens.LoginIdClaim);
        if (!Guid.TryParseExact(id, "D", out var loginId))
            return false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<AccountStore>();
            return await store.FindActiveRefreshTokenAsync(loginId, cancellationToken) is not null;
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            throw new HubException("登录会话暂时无法校验，请稍后重试。");
        }
    }
}

public sealed record AllOrderSyncStartResult(bool Accepted, int StartedCount, int BusyCount, string? Error);
