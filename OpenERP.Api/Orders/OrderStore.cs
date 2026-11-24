using Mapster;
using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Orders;

public sealed record OrderSaveCounts(int Added, int Updated, int Unchanged);

public sealed class OrderStore(AppDbContext db, IOrderChangeCache? cache = null)
{
    private static readonly TypeAdapterConfig SnapshotMapping = CreateSnapshotMapping();

    public async Task<OpResult<OrderSaveCounts>> SaveAsync(IReadOnlyList<OrderSnapshot> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
            return new OrderSaveCounts(0, 0, 0);

        // 订单收入按分保存，重放比较与写入使用同一个金额。
        if (!TryNormalizeIncome(orders, out orders))
            return OpResults.Err("保存订单失败，收入金额超出可保存的范围。");

        var ids = orders.Select(order => order.ErpId).ToArray();
        Dictionary<string, Guid> revisions = [];
        IOrderChangeBatch? changes = null;
        if (cache is not null)
        {
            var storedOrders = await db.Orders.IgnoreQueryFilters()
                .Where(order => ids.Contains(order.ErpId))
                .Select(order => new { order.ErpId, order.SyncRevision, IsDeleted = order.DeletedAt != null })
                .ToDictionaryAsync(order => order.ErpId, cancellationToken);
            revisions = storedOrders.Values.Where(order => !order.IsDeleted)
                .ToDictionary(order => order.ErpId, order => order.SyncRevision);
            changes = await cache.FindChangesAsync(orders, revisions, cancellationToken);
            ids = changes.Candidates.Select(order => order.ErpId).Where(storedOrders.ContainsKey).ToArray();
        }

        var existingOrders = ids.Length == 0 ? [] : await db.Orders
            .IgnoreQueryFilters()
            .Where(order => ids.Contains(order.ErpId))
            .Include(order => order.PlatformOrders)
            .Include(order => order.Lines)
            .AsSplitQuery()
            .ToDictionaryAsync(order => order.ErpId, cancellationToken);

        var added = 0;
        var updated = 0;
        var deletedAt = DateTimeOffset.UtcNow;
        foreach (var order in changes?.Candidates ?? orders)
        {
            var desired = CreateRecord(order);
            if (!existingOrders.TryGetValue(order.ErpId, out var existing))
            {
                desired.SyncRevision = Guid.NewGuid();
                db.Orders.Add(desired);
                revisions[order.ErpId] = desired.SyncRevision;
                added++;
                continue;
            }

            desired.SyncRevision = existing.SyncRevision;
            var orderChanged = ApplyValues(existing, desired);
            var platformOrdersChanged = ApplyChildren(
                existing.PlatformOrders, desired.PlatformOrders, child => child.PoNumber, deletedAt);
            var activeLines = existing.Lines.Where(line => line.DeletedAt is null).ToArray();
            var linesChanged = !HaveSameLines(activeLines, order.Lines)
                && ApplyChildren(existing.Lines, desired.Lines, child => child.Ordinal, deletedAt);

            if (orderChanged || platformOrdersChanged || linesChanged)
            {
                existing.SyncRevision = Guid.NewGuid();
                updated++;
            }

            revisions[order.ErpId] = existing.SyncRevision;
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

        if (changes is not null)
            await changes.ConfirmSavedAsync(revisions, cancellationToken);

        return new OrderSaveCounts(added, updated, orders.Count - added - updated);
    }

    private static bool TryNormalizeIncome(IReadOnlyList<OrderSnapshot> source, out IReadOnlyList<OrderSnapshot> normalized)
    {
        normalized = source;
        OrderSnapshot[]? changed = null;
        for (var i = 0; i < source.Count; i++)
        {
            if (source[i].IncomeCost is not { } amount)
                continue;
            var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (rounded is > 9999999999999999.99m or < -9999999999999999.99m)
                return false;
            if (rounded != amount)
            {
                changed ??= source.ToArray();
                changed[i] = source[i] with { IncomeCost = rounded };
            }
        }

        normalized = changed ?? source;
        return true;
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
