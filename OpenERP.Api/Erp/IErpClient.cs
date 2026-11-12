namespace OpenERP.Api.Erp;

/// <summary>私有 ERP 接入契约，由具体 ERP 客户端实现。</summary>
public interface IErpClient
{
    ErpConfiguration Configuration { get; }

    Task<ErpResult<ShopPage>> GetShopsAsync(int page, int pageSize, CancellationToken cancellationToken);
}
