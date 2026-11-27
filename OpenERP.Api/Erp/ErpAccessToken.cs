namespace OpenERP.Api.Erp;

/// <summary>一次上游操作使用的凭证快照；BaseUrl 为空表示公共版。</summary>
public sealed class ErpAccessToken(string value, string? baseUrl = null)
{
    public string Value { get; } = value;

    /// <summary>上游 ERP 根地址（无末尾斜杠）。</summary>
    public string BaseUrl { get; } = string.IsNullOrWhiteSpace(baseUrl)
        ? ErpConfiguration.UpstreamBaseUrl
        : baseUrl.TrimEnd('/');
}
