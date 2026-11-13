using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Orders;

public sealed record OrderSaveCounts(int Added, int Updated, int Unchanged);

public sealed class OrderStore(AppDbContext db)
{
    public async Task<OpResult<OrderSaveCounts>> SaveAsync(IReadOnlyList<OrderSnapshot> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
            return new OrderSaveCounts(0, 0, 0);

        var erpIds = orders.Select(order => order.ErpId).ToArray();
        var existingOrders = await db.Orders
            .Where(order => erpIds.Contains(order.ErpId))
            .Include(order => order.PlatformOrders)
            .Include(order => order.Lines)
            .AsSplitQuery()
            .ToDictionaryAsync(order => order.ErpId, cancellationToken);

        var added = 0;
        var updated = 0;
        foreach (var order in orders)
        {
            var desired = CreateRecord(order);
            if (!existingOrders.TryGetValue(order.ErpId, out var existing))
            {
                db.Orders.Add(desired);
                added++;
                continue;
            }

            var orderChanged = ApplyValues(existing, desired);
            var platformOrdersChanged = ApplyChildren(existing.PlatformOrders, desired.PlatformOrders, child => child.PoNumber);
            var linesChanged = !HaveSameLines(existing.Lines, order.Lines)
                && ApplyChildren(existing.Lines, desired.Lines, child => child.Ordinal);

            if (orderChanged || platformOrdersChanged || linesChanged)
                updated++;
        }

        if (added + updated > 0)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return OpResults.Err("保存订单失败，请检查数据库后重试。");
            }
        }

        return new OrderSaveCounts(added, updated, orders.Count - added - updated);
    }

    private bool ApplyValues<TEntity>(TEntity existing, TEntity desired) where TEntity : class
    {
        var entry = db.Entry(existing);
        entry.CurrentValues.SetValues(desired);
        return entry.State == EntityState.Modified;
    }

    private bool ApplyChildren<TEntity, TKey>(
        List<TEntity> existing, List<TEntity> desired, Func<TEntity, TKey> getKey)
        where TEntity : class where TKey : notnull
    {
        var remaining = existing.ToDictionary(getKey);
        var changed = false;
        foreach (var child in desired)
        {
            if (remaining.Remove(getKey(child), out var current))
            {
                changed |= ApplyValues(current, child);
                continue;
            }

            existing.Add(child);
            changed = true;
        }

        foreach (var removed in remaining.Values)
            existing.Remove(removed);

        return changed || remaining.Count > 0;
    }

    private static bool HaveSameLines(IReadOnlyList<OrderLineRecord> existing, IReadOnlyList<OrderLineSnapshot> desired)
    {
        if (existing.Count != desired.Count)
            return false;

        var counts = desired.CountBy(line => line).ToDictionary();
        foreach (var line in existing)
        {
            var value = new OrderLineSnapshot(line.ProductId, line.Oguid, line.SkuId, line.SkuCode,
                line.Quantity, line.Disabled, line.Appended);
            if (!counts.TryGetValue(value, out var count) || count == 0)
                return false;

            counts[value] = count - 1;
        }

        return true;
    }

    private static OrderRecord CreateRecord(OrderSnapshot order)
    {
        var record = new OrderRecord
        {
            ErpId = order.ErpId,
            OrderNumber = order.OrderNumber,
            Sid = order.Sid,
            Platform = order.Platform,
            Status = order.Status,
            CreatedRaw = order.Times.Created?.Raw,
            CreatedSource = order.Times.Created?.Source,
            PaidRaw = order.Times.Paid?.Raw,
            PaidSource = order.Times.Paid?.Source,
            DispatchedRaw = order.Times.Dispatched?.Raw,
            DispatchedSource = order.Times.Dispatched?.Source,
            RefundedRaw = order.Times.Refunded?.Raw,
            RefundedSource = order.Times.Refunded?.Source
        };

        record.PlatformOrders.AddRange(order.PlatformOrders.Select(platformOrder => new PlatformOrderRecord
        {
            ErpId = order.ErpId,
            PoNumber = platformOrder.PoNumber,
            Sid = platformOrder.Sid,
            StatusCode = platformOrder.StatusCode,
            StatusText = platformOrder.StatusText
        }));
        record.Lines.AddRange(order.Lines.Select((line, ordinal) => new OrderLineRecord
        {
            ErpId = order.ErpId,
            Ordinal = ordinal,
            ProductId = line.ProductId,
            Oguid = line.Oguid,
            SkuId = line.SkuId,
            SkuCode = line.SkuCode,
            Quantity = line.Quantity,
            Disabled = line.Disabled,
            Appended = line.Appended
        }));
        return record;
    }
}
