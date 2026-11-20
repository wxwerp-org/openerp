using System.Text.RegularExpressions;
using Mapster;

namespace OpenERP.Api.Authentication;

/// <summary>已验证的资料字段，仅允许修改姓名、电话和邮箱。</summary>
internal sealed partial record ProfileChanges(string Name, string? Phone, string? Email)
{
    internal static ProfileChanges Normalize(string? name, string? phone, string? email) =>
        new(name?.Trim() ?? "", TrimOptional(phone), TrimOptional(email));

    internal Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Name.Length is 0 or > 20)
            errors["name"] = ["姓名必填且不得超过 20 个字符。"];
        if (Phone is { Length: > 20 })
            errors["phone"] = ["电话不得超过 20 个字符。"];
        if (Email is not null && !EmailPattern().IsMatch(Email))
            errors["email"] = ["邮箱格式无效。"];
        return errors;
    }

    internal void Apply(UserAccount account) => this.Adapt(account);

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+$")]
    private static partial Regex EmailPattern();
}

/// <summary>用户管理可读取的账号资料。</summary>
internal sealed record UserResponse
{
    public Guid Id { get; init; }
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public string Role { get; init; } = "";
}

/// <summary>当前用户及其会话到期时间。</summary>
internal sealed record AccountProfile
{
    public Guid Id { get; init; }
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public string Role { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}
