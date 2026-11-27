using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Erp;
using OpResult;

namespace OpenERP.Api.Orders.Sources;

/// <summary>读取订单所需的店铺身份：旺销王店铺编号、所属平台和名称，不含任何凭据。</summary>
public sealed record PlatformShopRef(int Sid, string Platform, string Name);

/// <summary>
/// 平台订单来源：每个平台一个实现，通过平台接口读取某家店铺的订单并转换为统一的订单快照。
/// 同步任务、进度与定时调度只依赖这个契约，不关心各平台不同的订单结构。
/// </summary>
public interface IPlatformOrderSource
{
    /// <summary>平台名称，与授权店铺目录里的平台名称一致。</summary>
    string Platform { get; }

    Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        PlatformShopRef shop, Action<OrderReadProgress>? report, CancellationToken cancellationToken);
}

/// <summary>按平台名称解析订单来源。</summary>
public sealed class PlatformOrderSources(IEnumerable<IPlatformOrderSource> sources)
{
    private readonly Dictionary<string, IPlatformOrderSource> _sources =
        sources.ToDictionary(source => source.Platform, StringComparer.Ordinal);

    public IReadOnlyCollection<string> Platforms => _sources.Keys;

    public IPlatformOrderSource? Find(string platform) => _sources.GetValueOrDefault(platform);
}

/// <summary>按店铺编号查找授权店铺目录里的店铺，便于同步任务不依赖数据库验证流程。</summary>
internal interface IPlatformShops
{
    Task<OpResult<PlatformShopRef>> FindAsync(int sid, CancellationToken cancellationToken);
}

internal sealed class PlatformShopDirectory(AppDbContext db) : IPlatformShops
{
    public async Task<OpResult<PlatformShopRef>> FindAsync(int sid, CancellationToken cancellationToken)
    {
        var shop = await db.Shops.AsNoTracking().Where(row => row.Sid == sid)
            .Select(row => new PlatformShopRef(row.Sid, row.Platform, row.Name)).FirstOrDefaultAsync(cancellationToken);
        return shop is null ? OpResults.Err("店铺不存在或已从授权店铺中移除。") : shop;
    }
}
