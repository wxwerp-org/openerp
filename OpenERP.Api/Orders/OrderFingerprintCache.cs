using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;

namespace OpenERP.Api.Orders;

internal static class OrderFingerprintCache
{
    // 分块限制单个 Redis 值的大小，一次同步不需要为每张订单往返 Redis。
    private const int PageSize = 256;
    private static readonly HybridCacheEntryOptions ReadOptions = new()
    {
        LocalCacheExpiration = TimeSpan.FromHours(2),
        Flags = HybridCacheEntryFlags.DisableUnderlyingData | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };
    private static readonly HybridCacheEntryOptions WriteOptions = new()
    {
        Expiration = TimeSpan.FromDays(1),
        LocalCacheExpiration = TimeSpan.FromHours(2)
    };

    internal sealed record Page(string Key, Dictionary<string, string> Fingerprints);

    internal static async Task<(IReadOnlyList<OrderSnapshot> Candidates, List<Page> Pages)> FindChangesAsync(
        HybridCache cache, IReadOnlyList<OrderSnapshot> orders, Dictionary<string, Guid> revisions,
        ILogger? logger, CancellationToken ct)
    {
        List<OrderSnapshot> candidates = [];
        List<Page> pages = [];
        foreach (var shop in orders.GroupBy(order => order.Sid))
        {
            var chunks = shop.OrderByDescending(order => order.ErpId, StringComparer.Ordinal).Chunk(PageSize).ToArray();
            var keys = Enumerable.Range(0, chunks.Length).Select(page => $"orders:fingerprint:v1:{shop.Key}:{page}").ToArray();
            var previousPages = new Dictionary<Guid, string>?[chunks.Length];
            var confirmed = new Dictionary<Guid, string>();
            if (shop.Any(order => revisions.ContainsKey(order.ErpId)))
            {
                for (var index = 0; index < keys.Length; index++)
                {
                    try
                    {
                        var saved = await cache.GetOrCreateAsync<Dictionary<Guid, string>?>(keys[index],
                            static _ => ValueTask.FromResult<Dictionary<Guid, string>?>(null), ReadOptions, cancellationToken: ct);
                        previousPages[index] = saved;
                        if (saved is not null)
                            foreach (var entry in saved)
                                confirmed[entry.Key] = entry.Value;
                    }
                    catch (Exception error) when (IsCacheFailure(error))
                    {
                        logger?.LogWarning("订单指纹缓存读取失败，剩余订单改从数据库比对。{ErrorType}", error.GetType().Name);
                        break;
                    }
                }
            }

            // 分块只决定 Redis 值的大小；新订单挤动边界后仍能命中其他分块的旧版本。
            for (var index = 0; index < chunks.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = chunks[index];
                var previousPage = previousPages[index];
                var publish = previousPage?.Count != chunk.Length;
                var fingerprints = new Dictionary<string, string>(chunk.Length);
                foreach (var order in chunk)
                {
                    var fingerprint = OrderSnapshotFingerprint.Compute(order);
                    fingerprints.Add(order.ErpId, fingerprint);
                    if (!revisions.TryGetValue(order.ErpId, out var revision) ||
                        !confirmed.TryGetValue(revision, out var previous) || previous != fingerprint)
                    {
                        candidates.Add(order);
                        publish = true;
                    }
                    else if (previousPage?.ContainsKey(revision) != true)
                    {
                        publish = true;
                    }
                }

                if (publish)
                    pages.Add(new(keys[index], fingerprints));
            }
        }

        return (candidates, pages);
    }

    internal static async Task PublishAsync(HybridCache cache, List<Page> pages, Dictionary<string, Guid> revisions,
        ILogger? logger, CancellationToken ct)
    {
        foreach (var page in pages)
        {
            var saved = page.Fingerprints.ToDictionary(pair => revisions[pair.Key], pair => pair.Value);
            try
            {
                await cache.SetAsync(page.Key, saved, WriteOptions, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 数据已提交；停止填充缓存即可返回真实的保存结果。
                return;
            }
            catch (Exception error) when (IsCacheFailure(error))
            {
                logger?.LogWarning("订单已保存，指纹缓存写入失败。{ErrorType}", error.GetType().Name);
                return;
            }
        }
    }

    private static bool IsCacheFailure(Exception error) =>
        error is RedisException or TimeoutException or JsonException or InvalidDataException;
}
