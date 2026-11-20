using Mapster;
using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Orders;

public sealed record OrderSaveCounts(int Added, int Updated, int Unchanged);

public sealed class OrderStore(AppDbContext db)
{
    private static readonly TypeAdapterConfig SnapshotMapping = CreateSnapshotMapping();

    public async Task<OpResult<OrderSaveCounts>> SaveAsync(IReadOnlyList<OrderSnapshot> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
            return new OrderSaveCounts(0, 0, 0);

        var erpIds = orders.Select(order => order.ErpId).ToArray();
        var existingOrders = await db.Orders
            .IgnoreQueryFilters()
            .Where(order => erpIds.Contains(order.ErpId))
            .Include(order => order.PlatformOrders)
            .Include(order => order.Lines)
            .AsSplitQuery()
            .ToDictionaryAsync(order => order.ErpId, cancellationToken);

        var added = 0;
        var updated = 0;
        var deletedAt = DateTimeOffset.UtcNow;
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
            var platformOrdersChanged = ApplyChildren(
                existing.PlatformOrders, desired.PlatformOrders, child => child.PoNumber, deletedAt);
            var activeLines = existing.Lines.Where(line => line.DeletedAt is null).ToArray();
            var linesChanged = !HaveSameLines(activeLines, order.Lines)
                && ApplyChildren(existing.Lines, desired.Lines, child => child.Ordinal, deletedAt);

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

    private bool ApplyValues<TEntity>(TEntity existing, TEntity desired) where TEntity : Entity
    {
        var entry = db.Entry(existing);
        var values = db.Entry(desired).CurrentValues;
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.Name != nameof(Entity.Id))
                property.CurrentValue = values[property.Metadata];
        }

        return entry.State == EntityState.Modified;
    }

    private bool ApplyChildren<TEntity, TKey>(
        List<TEntity> existing, List<TEntity> desired, Func<TEntity, TKey> getKey, DateTimeOffset deletedAt)
        where TEntity : Entity where TKey : notnull
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

        foreach (var removed in remaining.Values.Where(child => child.DeletedAt is null))
        {
            removed.DeletedAt = deletedAt;
            changed = true;
        }

        return changed;
    }

    private static bool HaveSameLines(IReadOnlyList<OrderLineRecord> existing, IReadOnlyList<OrderLineSnapshot> desired)
    {
        if (existing.Count != desired.Count)
            return false;

        var counts = desired.CountBy(line => line).ToDictionary();
        foreach (var line in existing)
        {
            var value = line.Adapt<OrderLineSnapshot>();
            if (!counts.TryGetValue(value, out var count) || count == 0)
                return false;

            counts[value] = count - 1;
        }

        return true;
    }

    private static OrderRecord CreateRecord(OrderSnapshot order)
    {
        var record = order.Adapt<OrderRecord>(SnapshotMapping);
        record.PlatformOrders.AddRange(order.PlatformOrders.Select(platformOrder => platformOrder.Adapt(
            new PlatformOrderRecord { ErpId = order.ErpId, PoNumber = platformOrder.PoNumber })));
        record.Lines.AddRange(order.Lines.Select((line, ordinal) => line.Adapt(
            new OrderLineRecord { ErpId = order.ErpId, Ordinal = ordinal })));
        return record;
    }

    private static TypeAdapterConfig CreateSnapshotMapping()
    {
        var mapping = new TypeAdapterConfig();
        mapping.NewConfig<OrderSnapshot, OrderRecord>()
            .Ignore(record => record.PlatformOrders)
            .Ignore(record => record.Lines)
            .Map(record => record.CreatedRaw, order => order.Times.Created!.Raw)
            .Map(record => record.CreatedSource, order => order.Times.Created!.Source)
            .Map(record => record.PaidRaw, order => order.Times.Paid!.Raw)
            .Map(record => record.PaidSource, order => order.Times.Paid!.Source)
            .Map(record => record.DispatchedRaw, order => order.Times.Dispatched!.Raw)
            .Map(record => record.DispatchedSource, order => order.Times.Dispatched!.Source)
            .Map(record => record.RefundedRaw, order => order.Times.Refunded!.Raw)
            .Map(record => record.RefundedSource, order => order.Times.Refunded!.Source);
        return mapping;
    }
}
