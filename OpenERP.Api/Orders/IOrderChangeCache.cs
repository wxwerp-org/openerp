namespace OpenERP.Api.Orders;

public interface IOrderChangeCache
{
    Task<IOrderChangeBatch> FindChangesAsync(IReadOnlyList<OrderSnapshot> orders,
        IReadOnlyDictionary<string, Guid> storedRevisions, CancellationToken ct);
}

/// <summary>需要与数据库比对的订单，以及本次成功保存后要确认的订单版本。</summary>
public interface IOrderChangeBatch
{
    IReadOnlyList<OrderSnapshot> Candidates { get; }
    Task ConfirmSavedAsync(IReadOnlyDictionary<string, Guid> savedRevisions, CancellationToken ct);
}
