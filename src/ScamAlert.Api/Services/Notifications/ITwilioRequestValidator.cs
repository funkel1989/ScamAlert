using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ScamAlert.Api.Services.Notifications;

public interface ITwilioRequestValidator
{
    bool IsValid(HttpRequest request, IFormCollection form);
}

public sealed class TwilioRequestValidator(IOptions<TwilioOptions> options) : ITwilioRequestValidator
{
    public bool IsValid(HttpRequest request, IFormCollection form)
    {
        var twilio = options.Value;
        if (!twilio.ValidateWebhookSignatures)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(twilio.AuthToken))
        {
            return false;
        }

        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var signatureHeader))
        {
            return false;
        }

        var expected = ComputeSignature(twilio.AuthToken, BuildUrl(twilio, request), form);
        return FixedTimeEquals(expected, signatureHeader.ToString());
    }

    private static string BuildUrl(TwilioOptions options, HttpRequest request)
    {
        var requestUri = $"{request.PathBase}{request.Path}{request.QueryString}";
        if (string.IsNullOrWhiteSpace(options.WebhookPublicBaseUrl))
        {
            return $"{request.Scheme}://{request.Host}{requestUri}";
        }

        return $"{options.WebhookPublicBaseUrl.TrimEnd('/')}{requestUri}";
    }

    public static string ComputeSignature(string authToken, string url, IFormCollection form)
    {
        var sb = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var values = form[key];
            foreach (var value in values)
            {
                sb.Append(key);
                sb.Append(value);
            }
        }

        var secretBytes = Encoding.UTF8.GetBytes(authToken);
        var dataBytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
