namespace OpenERP.Api.Authentication;

internal sealed class UserAccount : Entity
{
    internal const string AdminUsername = "admin";
    internal const string AdministratorRole = "Administrator";
    internal const string UserRole = "User";

    public string Username { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; set; }

    internal bool IsAdministrator => Username == AdminUsername;
    public string Role => IsAdministrator ? AdministratorRole : UserRole;
}

internal sealed class UserRefreshToken : Entity
{
    public Guid UserId { get; init; }
    public UserAccount User { get; init; } = null!;
    public byte[] TokenHash { get; init; } = null!;
    public DateTimeOffset ExpiresAt { get; init; }
}
