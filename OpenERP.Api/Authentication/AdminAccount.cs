namespace OpenERP.Api.Authentication;

internal sealed class AdminAccount
{
    internal const int SingletonId = 1;
    internal const string Username = "admin";

    public int Id { get; init; } = SingletonId;
    public string PasswordHash { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public string Name { get; set; } = "管理员";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

internal sealed class AdminSession
{
    public Guid Id { get; init; }
    public int AdminId { get; init; } = AdminAccount.SingletonId;
    public AdminAccount Admin { get; init; } = null!;
    public DateTimeOffset ExpiresAt { get; init; }
}
