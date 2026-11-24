namespace OpenERP.Api.Authentication;

/// <summary>人工分配随账号保存，目录被上游替换时仍保留平台和 SID。</summary>
internal sealed class UserShopAssignment
{
    public Guid UserId { get; init; }
    public required string Platform { get; init; }
    public int Sid { get; init; }
}
