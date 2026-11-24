using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Persistence;

/// <summary>迁移后为存量行回填订单时间和店名检索值。</summary>
internal static class ReadModelBackfill
{
    private const int BatchSize = 500;

    internal static async Task RunAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        while (await strategy.ExecuteAsync(ct => FillBatchAsync(db, ct), cancellationToken))
        {
        }
    }

    private static async Task<bool> FillBatchAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // 读取源值与写入派生值使用同一事务，避免覆盖并发同步写入的新值。
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var orders = await db.Orders.IgnoreQueryFilters().Where(order => order.CreatedAtTicks == null)
                .OrderBy(order => order.ErpId).Take(BatchSize).ToArrayAsync(cancellationToken);
            var shops = await db.Shops.IgnoreQueryFilters().Where(shop => shop.SearchName == null)
                .OrderBy(shop => shop.Id).Take(BatchSize).ToArrayAsync(cancellationToken);
            if (orders.Length == 0 && shops.Length == 0)
                return false;

            foreach (var order in orders)
                order.CreatedAtTicks = OrderSourceTime.ToTicks(order.CreatedRaw);
            foreach (var shop in shops)
                shop.SearchName = shop.Name.ToUpperInvariant();

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            // 下一批和瞬时故障重试都从数据库读起，已提交的批次不会重复处理。
            db.ChangeTracker.Clear();
        }
    }
}
