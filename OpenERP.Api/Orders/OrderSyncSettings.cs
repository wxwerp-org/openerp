using Microsoft.EntityFrameworkCore;
using OpResult;

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

/// <summary>定时同步设置实体；全表仅保留 Id 为 1 的一行。</summary>
public sealed class OrderSyncSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool Enabled { get; set; }

    public int[] ShopSids { get; set; } = [];
}

public sealed class OrderSyncSettingsStore(AppDbContext db) : IOrderSyncSettingsStore
{
    public async Task<OrderSyncSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.OrderSyncSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == OrderSyncSettings.SingletonId, cancellationToken);
        return settings is null
            ? new OrderSyncSettingsSnapshot(false, [])
            : new OrderSyncSettingsSnapshot(settings.Enabled, settings.ShopSids);
    }

    public async Task<OpResult<OrderSyncSettingsSnapshot>> SaveAsync(
        bool enabled, IReadOnlyList<int> shopSids, CancellationToken cancellationToken)
    {
        var normalized = shopSids.Distinct().Order().ToArray();
        var settings = await db.OrderSyncSettings
            .SingleOrDefaultAsync(row => row.Id == OrderSyncSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new OrderSyncSettings();
            db.OrderSyncSettings.Add(settings);
        }

        settings.Enabled = enabled;
        settings.ShopSids = normalized;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return OpResults.Err("保存定时同步设置失败，请重试。");
        }

        return new OrderSyncSettingsSnapshot(settings.Enabled, settings.ShopSids);
    }
}
