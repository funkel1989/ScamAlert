namespace ScamAlert.Api.Services.Stripe;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool SkipPaymentForDevelopment { get; set; }
}
