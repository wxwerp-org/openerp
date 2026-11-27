using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using OpResult;
using OpenERP.Api.Persistence;

namespace OpenERP.Api.Orders;

/// <summary>定时同步设置快照；店铺按 SID 去重保存。</summary>
public sealed record OrderSyncSettingsSnapshot(bool Enabled, IReadOnlyList<int> ShopSids);

/// <summary>定时同步设置存储契约，便于调度器和接口独立验证。</summary>
public interface IOrderSyncSettingsStore
{
    Task<OrderSyncSettingsSnapshot> GetAsync(CancellationToken cancellationToken);

    Task<OpResult<OrderSyncSettingsSnapshot>> SaveAsync(
        bool enabled, IReadOnlyList<int> shopSids, CancellationToken cancellationToken);
}

public sealed class OrderSyncSettingsStore(AppDbContext db) : IOrderSyncSettingsStore
{
    public async Task<OrderSyncSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.OrderSyncSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return settings is null
            ? new OrderSyncSettingsSnapshot(false, [])
            : new OrderSyncSettingsSnapshot(settings.Enabled, settings.ShopSids);
    }

    public async Task<OpResult<OrderSyncSettingsSnapshot>> SaveAsync(
        bool enabled, IReadOnlyList<int> shopSids, CancellationToken cancellationToken)
    {
        var normalized = shopSids.Distinct().Order().ToArray();
        try
        {
            await SingletonSettings.SaveAsync(db, new OrderSyncSettings { Enabled = enabled, ShopSids = normalized },
                ct => db.OrderSyncSettings.IgnoreQueryFilters().ExecuteUpdateAsync(setters => setters
                    .SetProperty(settings => settings.Enabled, enabled)
                    .SetProperty(settings => settings.ShopSids, normalized)
                    .SetProperty(settings => settings.DeletedAt, (DateTimeOffset?)null), ct), cancellationToken);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        {
            return OpResults.Err("保存定时同步设置失败，请重试。");
        }

        return new OrderSyncSettingsSnapshot(enabled, normalized);
    }
}
