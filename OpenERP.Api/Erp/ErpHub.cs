using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Erp;

/// <summary>ERP 长时交互 Hub；只承载店铺同步，普通读取仍走 HTTP。</summary>
[Authorize]
public sealed class ErpHub(IShopSyncJob job, IServiceScopeFactory scopeFactory) : Hub
{
    public const string ShopSyncUpdated = "ShopSyncUpdated";

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
        await base.OnConnectedAsync();
    }

    public async Task<ShopSyncStartResult> StartShopSync()
    {
        // 官方建议的“连接内按当前数据校验”补偿：只在命令执行前检查会话，不逐消息重跑认证。
        if (!await IsSessionActiveAsync(Context.ConnectionAborted))
        {
            Context.Abort();
            throw new HubException("登录会话已失效，请重新登录。");
        }

        var result = await job.TryStartAsync(Context.ConnectionAborted);
        if (result.IsErr)
            return new ShopSyncStartResult(false, await ReadStatusAsync(Context.ConnectionAborted), result.Error.Message);

        return new ShopSyncStartResult(true, result.Value, null);
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
