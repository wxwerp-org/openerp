using OpResult;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Erp;

/// <summary>私有 ERP 接入契约，由具体 ERP 客户端实现。</summary>
public interface IErpClient
{
    ErpConfiguration Configuration { get; }

    Task<(OpResult<ShopPage> Result, int Status)> GetShopsAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<OpResult<IReadOnlyList<ErpShop>>> GetAllShopsAsync(CancellationToken cancellationToken);

    Task<OpResult<OrderImportBatch>> ReadOrdersAsync(
        int sid, Action<OrderReadProgress>? report, CancellationToken cancellationToken);
}
