namespace ScamAlert.Api.Services.Notifications;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromPhoneNumber { get; set; } = string.Empty;
    public string? StatusCallbackBaseUrl { get; set; }
    public bool ValidateWebhookSignatures { get; set; } = true;
    public string? WebhookPublicBaseUrl { get; set; }
}
