using Mapster;
using Microsoft.EntityFrameworkCore;
using OpResult;

namespace OpenERP.Api.Erp;

/// <summary>店铺与授权状态同步的当前或最近一次结果。</summary>
public sealed record ShopSyncStatus(
    bool Running,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Total,
    int Checked,
    int Valid,
    int Invalid,
    int Unknown,
    string? Error,
    long Version);

/// <summary>手动触发同步的结果；被拒绝时不返回目录同步状态。</summary>
public sealed record ShopSyncStartResult(bool Accepted, ShopSyncStatus? Status, string? Error);

/// <summary>同步任务契约，便于 Hub、接口和调度器共用。</summary>
public interface IShopSyncJob
{
    ShopSyncStatus Status { get; }

    Task<OpResult<ShopSyncStatus>> TryStartAsync(CancellationToken cancellationToken);
}

/// <summary>同步任务推送契约；SignalR 实现失败不应影响落库。</summary>
internal interface IShopSyncNotifier
{
    Task PublishAsync(ShopSyncStatus status, CancellationToken cancellationToken);
}

/// <summary>店铺同步周期与首刷延迟配置。</summary>
public sealed class ShopSyncOptions
{
    public const int DefaultIntervalMinutes = 60;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;

    public int InitialDelaySeconds { get; set; } = 5;
}

/// <summary>读取同步状态：运行中取内存进度，空闲时取数据库最近一次结果。</summary>
internal static class ShopSyncStatusQueries
{
    private static readonly TypeAdapterConfig StatusMapping = CreateStatusMapping();

    public static async Task<ShopSyncStatus> ReadAsync(
        IShopSyncJob job, AppDbContext db, CancellationToken cancellationToken)
    {
        var current = job.Status;
        if (current.Running)
            return current;

        var row = await db.ShopSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return new ShopSyncStatus(false, null, null, 0, 0, 0, 0, 0, null, 0);

        return row.Adapt<ShopSyncStatus>(StatusMapping);
    }

    private static TypeAdapterConfig CreateStatusMapping()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<ShopSyncState, ShopSyncStatus>()
            .MapToConstructor(true)
            .Map(destination => destination.StartedAt, row => row.LastStartedAt)
            .Map(destination => destination.CompletedAt, row => row.LastCompletedAt)
            .Map(destination => destination.Checked, row => row.Total)
            .Map(destination => destination.Error, row => row.LastError);
        return config;
    }
}
