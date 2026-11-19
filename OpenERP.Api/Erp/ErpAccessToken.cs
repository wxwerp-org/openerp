namespace OpenERP.Api.Erp;

/// <summary>一次上游操作使用的凭证快照。</summary>
public sealed class ErpAccessToken(string value)
{
    public string Value { get; } = value;
}
