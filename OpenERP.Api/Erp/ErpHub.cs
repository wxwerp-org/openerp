using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

/// <summary>ERP 长时交互 Hub；承载店铺与订单同步。</summary>
[Authorize]
public sealed class ErpHub(IShopSyncJob job, IServiceScopeFactory scopeFactory, OrderSyncJobs? orders = null) : Hub
{
    public const string ShopSyncUpdated = "ShopSyncUpdated";
    public const string OrderSyncUpdated = "OrderSyncUpdated";
    public const string OrderSyncSnapshot = "OrderSyncSnapshot";

    public override async Task OnConnectedAsync()
    {
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
        if (orders is not null)
            await Clients.Caller.SendAsync(OrderSyncSnapshot, orders.Snapshot(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task<ShopSyncStartResult> StartShopSync()
    {
        await EnsureActiveSessionAsync();
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

        await using var scope = scopeFactory.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
        var configuration = await tokens.GetConfigurationAsync(Context.ConnectionAborted);
        if (configuration.IsErr)
            return new(false, orders.Latest(sid), configuration.Error.Message);
        if (!configuration.Value.Configured)
            return new(false, orders.Latest(sid), "尚未配置私有 ERP AccessToken");

        var started = orders.Start(sid);
        return started.IsErr
            ? new(false, orders.Latest(sid), started.Error.Message)
            : new(true, started.Value, null);
    }

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
