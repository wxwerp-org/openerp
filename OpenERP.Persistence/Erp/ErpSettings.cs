namespace OpenERP.Api.Erp;

internal sealed class ErpSettings : Entity
{
    public required string ProtectedAccessToken { get; set; }
}
