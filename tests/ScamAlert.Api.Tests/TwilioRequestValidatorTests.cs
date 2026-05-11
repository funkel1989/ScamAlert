using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Notifications;

namespace ScamAlert.Api.Tests;

public sealed class TwilioRequestValidatorTests
{
    [Fact]
    public void IsValid_returns_true_for_matching_signature()
    {
        var options = Options.Create(new TwilioOptions
        {
            AuthToken = "test-token",
            ValidateWebhookSignatures = true,
            WebhookPublicBaseUrl = "https://example.test"
        });

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("ignored.local");
        context.Request.Path = "/api/webhooks/twilio/inbound-sms";
        context.Request.QueryString = QueryString.Empty;
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["Body"] = "ACK ABC123",
            ["From"] = "+15555550100"
        });

        var url = "https://example.test/api/webhooks/twilio/inbound-sms";
        var signature = TwilioRequestValidator.ComputeSignature("test-token", url, form);
        context.Request.Headers["X-Twilio-Signature"] = signature;

        var validator = new TwilioRequestValidator(options);
        var isValid = validator.IsValid(context.Request, form);

        Assert.True(isValid);
    }

    [Fact]
    public void IsValid_returns_false_for_bad_signature()
    {
        var options = Options.Create(new TwilioOptions
        {
            AuthToken = "test-token",
            ValidateWebhookSignatures = true,
            WebhookPublicBaseUrl = "https://example.test"
        });

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        context.Request.Path = "/api/webhooks/twilio/status";
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["MessageSid"] = "SM123"
        });
        context.Request.Headers["X-Twilio-Signature"] = "invalid";

        var validator = new TwilioRequestValidator(options);
        var isValid = validator.IsValid(context.Request, form);

        Assert.False(isValid);
    }
}
