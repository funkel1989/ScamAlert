namespace ScamAlert.Api.Services.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";
    public string Provider { get; set; } = "JwtBearer";
    public JwtAuthOptions Jwt { get; set; } = new();
    public LockoutOptions Lockout { get; set; } = new();
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
    public MicrosoftLoginOptions Microsoft { get; set; } = new();
}

public sealed class MicrosoftLoginOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class JwtAuthOptions
{
    public string Issuer { get; set; } = "ScamAlert.Api";
    public string Audience { get; set; } = "ScamAlert.Client";
    public string SigningKey { get; set; } = "<CHANGE_ME_PRODUCTION_KEY_MIN_32_CHARACTERS>";
    public int AccessTokenMinutes { get; set; } = 60;
    public bool RequireHttpsMetadata { get; set; } = true;
}

public sealed class LockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class BootstrapAdminOptions
{
    public bool Enabled { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string[] Roles { get; set; } = ["admin", "operator"];
}
