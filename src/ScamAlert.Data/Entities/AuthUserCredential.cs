namespace ScamAlert.Data.Entities;

public sealed class AuthUserCredential
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string RolesCsv { get; set; } = "operator";
    public string CustomerScopeCsv { get; set; } = "*";
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
