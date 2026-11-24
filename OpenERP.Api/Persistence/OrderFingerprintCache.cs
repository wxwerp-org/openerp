using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using OpenERP.Api.Orders;
using StackExchange.Redis;

namespace OpenERP.Api.Persistence;

internal static class OrderFingerprintCacheRegistration
{
    internal static void AddOrderFingerprintCache(this IServiceCollection services)
    {
        services.AddHybridCache();
        services.AddSingleton<IOrderChangeCache, OrderFingerprintCache>();
    }
}

internal sealed class OrderFingerprintCache(HybridCache cache, ILogger<OrderFingerprintCache>? logger = null) : IOrderChangeCache
{
    private sealed record Page(string Key, IReadOnlyDictionary<string, string> Fingerprints);

    private sealed class ChangeBatch(OrderFingerprintCache owner, IReadOnlyList<OrderSnapshot> candidates,
        IReadOnlyList<Page> pages) : IOrderChangeBatch
    {
        public IReadOnlyList<OrderSnapshot> Candidates => candidates;
        public Task ConfirmSavedAsync(IReadOnlyDictionary<string, Guid> savedRevisions, CancellationToken ct) =>
            owner.PublishAsync(pages, savedRevisions, ct);
    }

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

    public async Task<IOrderChangeBatch> FindChangesAsync(
        IReadOnlyList<OrderSnapshot> orders, IReadOnlyDictionary<string, Guid> revisions, CancellationToken ct)
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

        return new ChangeBatch(this, candidates, pages);
    }

    private async Task PublishAsync(IReadOnlyList<Page> pages, IReadOnlyDictionary<string, Guid> revisions, CancellationToken ct)
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
