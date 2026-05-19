namespace ScamAlert.Data.Entities;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
