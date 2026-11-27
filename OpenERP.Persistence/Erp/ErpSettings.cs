namespace OpenERP.Api.Erp;

internal sealed class ErpSettings : Entity
{
    public required string ProtectedAccessToken { get; set; }

    /// <summary>独立部署的上游地址；为空表示公共版。</summary>
    public string? BaseUrl { get; set; }
}
