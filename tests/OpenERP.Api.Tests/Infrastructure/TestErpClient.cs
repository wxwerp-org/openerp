using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Tests;

/// <summary>可替换 ERP 行为的测试客户端，只实现被测路径。</summary>
internal sealed class TestErpClient : IErpClient
{
    public ErpConfiguration Configuration { get; init; } = new(true, "https://erp.test");

    public Func<int, CancellationToken, Task<OpResult<OrderImportBatch>>> ReadOrders { get; init; } =
        (_, _) => Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));

    public Func<CancellationToken, Task<OpResult<IReadOnlyList<ErpShop>>>> GetAllShops { get; init; } =
        _ => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([]));

    public Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(
        int page, int pageSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("测试不经过分页店铺接口。");

    public Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(CancellationToken cancellationToken) =>
        GetAllShops(cancellationToken);

    public Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        int sid, Action<OrderReadProgress>? report, CancellationToken cancellationToken) =>
        ReadOrders(sid, cancellationToken);
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
