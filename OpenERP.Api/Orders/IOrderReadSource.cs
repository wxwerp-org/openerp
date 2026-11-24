namespace OpenERP.Api.Orders;

/// <summary>订单列表与报表用同一个快照读取可组合的订单时间投影。</summary>
internal interface IOrderReadSource
{
    IQueryable<OrderHeader> ReadHeaders();

    Task<T> ReadSnapshotAsync<T>(Func<Task<T>> read, CancellationToken ct);
}

internal sealed record OrderHeader(string ErpId, int Sid, string Platform,
    DateTime? CreatedAt,
    DateOnly? CreatedDate,
    DateTime? DispatchedAt,
    string? CreatedRaw, string? DispatchedRaw);
