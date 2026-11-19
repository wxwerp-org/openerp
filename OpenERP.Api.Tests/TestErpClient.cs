using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Tests;

/// <summary>可替换 ERP 行为的测试客户端，只实现被测路径。</summary>
internal sealed class TestErpClient : IErpClient
{
    public Func<int, ErpAccessToken, CancellationToken, Task<OpResult<OrderImportBatch>>> ReadOrders { get; init; } =
        (_, _, _) => Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));

    public Func<string?, ErpAccessToken, CancellationToken, Task<OpResult<IReadOnlyList<ErpShop>>>> GetAllShops { get; init; } =
        (_, _, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([]));

    public Func<string, ErpAccessToken, CancellationToken, Task<OpResult<ErpShopStatus>>> GetShopStatus { get; init; } =
        (_, _, _) => Task.FromResult(OpResults.Ok(new ErpShopStatus(null, null, null)));

    public Func<ErpAccessToken, CancellationToken, Task<(OpResult<IReadOnlyList<ErpPlatform>> Result, int Status)>> GetPlatforms
        { get; init; } = (_, _) => Task.FromResult((OpResults.Ok<IReadOnlyList<ErpPlatform>>([]), 200));

    public Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(
        int page, int pageSize, string? platform, ErpAccessToken accessToken, CancellationToken cancellationToken) =>
        Task.FromResult((OpResults.Ok(new ShopPage([], page, pageSize, 0, 0)), 200));

    public Task<(OpResult<IReadOnlyList<ErpPlatform>> Result, int Status)> GetPlatformsAsync(
        ErpAccessToken accessToken, CancellationToken cancellationToken) =>
        GetPlatforms(accessToken, cancellationToken);

    public Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(
        string? platform, ErpAccessToken accessToken, CancellationToken cancellationToken) =>
        GetAllShops(platform, accessToken, cancellationToken);

    public Task<OpResult<ErpShopStatus>> GetShopStatusAsync(
        string sid, ErpAccessToken accessToken, CancellationToken cancellationToken) =>
        GetShopStatus(sid, accessToken, cancellationToken);

    public Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        int sid, Action<OrderReadProgress>? report, ErpAccessToken accessToken, CancellationToken cancellationToken) =>
        ReadOrders(sid, accessToken, cancellationToken);
}

internal sealed class TestErpTokenStore(string value = "") : IErpTokenStore
{
    private string _value = value.Trim();
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OpResults.Ok(new ErpConfiguration(_value.Length > 0, ErpConfiguration.UpstreamBaseUrl)));

    public Task<OpResult<ErpAccessToken>> GetAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult<OpResult<ErpAccessToken>>(_value.Length == 0
            ? OpResults.Err("尚未配置私有 ERP AccessToken。")
            : new ErpAccessToken(_value));
    }

    public Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken)
    {
        _value = accessToken;
        return Task.FromResult(OpResults.Ok());
    }
}

internal sealed class FailingErpTokenStore : IErpTokenStore
{
    public Task<OpResult<ErpConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken) =>
        Task.FromResult<OpResult<ErpConfiguration>>(OpResults.Err("读取私有 ERP 配置失败，请稍后重试。"));

    public Task<OpResult<ErpAccessToken>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<OpResult<ErpAccessToken>>(OpResults.Err("读取私有 ERP AccessToken 失败，请稍后重试。"));

    public Task<OpResult.OpResult> SaveAsync(string accessToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>内存版定时同步设置存储，记录最后一次保存结果。</summary>
internal sealed class TestSettingsStore : IOrderSyncSettingsStore
{
    private OrderSyncSettingsSnapshot _current = new(false, []);

    public OrderSyncSettingsSnapshot? Saved { get; private set; }

    public bool FailSave { get; set; }

    public Task<OrderSyncSettingsSnapshot> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_current);

    public Task<OpResult<OrderSyncSettingsSnapshot>> SaveAsync(
        bool enabled, IReadOnlyList<int> shopSids, CancellationToken cancellationToken)
    {
        if (FailSave)
            return Task.FromResult<OpResult<OrderSyncSettingsSnapshot>>(OpResults.Err("保存定时同步设置失败，请重试。"));

        _current = new(enabled, shopSids.Distinct().Order().ToArray());
        Saved = _current;
        return Task.FromResult(OpResults.Ok(_current));
    }
}

internal static class TestWait
{
    public static async Task<T> EventuallyAsync<T>(
        Func<OpResult<T>> start, string failure, CancellationToken cancellationToken) where T : notnull
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var result = start();
            if (!result.IsErr)
                return result.Value;
            if (DateTime.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException($"{failure}：{result.Error.Message}");

            await Task.Delay(10, cancellationToken);
        }
    }

    public static async Task UntilAsync(Func<bool> condition, string failure, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException(failure);

            await Task.Delay(10, cancellationToken);
        }
    }
}
