namespace ScamAlert.Api.Services.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = "noreply@localhost";
    public string FromDisplayName { get; set; } = "ScamAlert";
    public string? SendGridApiKey { get; set; }
}
