using System.Data.Common;
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

/// <summary>定时同步设置实体；全表最多保留一行。</summary>
public sealed class OrderSyncSettings : Entity
{
    public bool Enabled { get; set; }

    public int[] ShopSids { get; set; } = [];
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
            // 单条插入或更新语句原子替换整份配置，避免并发保存把 Enabled 和 ShopSids 合并成用户未提交的组合。
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "OrderSyncSettings" ("Id", "Enabled", "ShopSids")
                VALUES ({Guid.CreateVersion7()}, {enabled}, {normalized})
                ON CONFLICT ("Singleton") DO UPDATE
                SET "Enabled" = EXCLUDED."Enabled", "ShopSids" = EXCLUDED."ShopSids", "DeletedAt" = NULL
                """, cancellationToken);
        }
        catch (DbException)
        {
            return OpResults.Err("保存定时同步设置失败，请重试。");
        }

        return new OrderSyncSettingsSnapshot(enabled, normalized);
    }
}
