using OpResult;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

/// <summary>私有 ERP 接入契约，由具体 ERP 客户端实现。</summary>
public interface IErpClient
{
    Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(
        int page, int pageSize, string? platform, ErpAccessToken accessToken, CancellationToken cancellationToken);

    Task<(OpResult<IReadOnlyList<ErpPlatform>> Result, int Status)> GetPlatformsAsync(
        ErpAccessToken accessToken, CancellationToken cancellationToken);

    Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(
        string? platform, ErpAccessToken accessToken, CancellationToken cancellationToken);

    Task<OpResult<ErpShopStatus>> GetShopStatusAsync(
        string sid, ErpAccessToken accessToken, CancellationToken cancellationToken);

    Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        int sid, Action<OrderReadProgress>? report, ErpAccessToken accessToken, CancellationToken cancellationToken);
}
