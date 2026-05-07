using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace ScamAlert.Api.Services.Notifications;

public sealed class TwilioNotificationGateway(
    HttpClient httpClient,
    IOptions<TwilioOptions> options,
    ILogger<TwilioNotificationGateway> logger) : INotificationGateway
{
    public async Task<GatewayResult> NotifyContactAsync(ContactNotification notification, CancellationToken cancellationToken)
    {
        var twilioOptions = options.Value;
        var callbackUrl = BuildCallbackUrl(twilioOptions.StatusCallbackBaseUrl, notification.NotificationAttemptId);
        var body = $"{notification.Message} Reply with ACK {notification.AcknowledgmentToken} to confirm.";

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{twilioOptions.AccountSid}/Messages.json");

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilioOptions.AccountSid}:{twilioOptions.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var form = new Dictionary<string, string>
        {
            ["To"] = notification.PhoneNumber,
            ["From"] = twilioOptions.FromPhoneNumber,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(callbackUrl))
        {
            form["StatusCallback"] = callbackUrl;
        }

        request.Content = new FormUrlEncodedContent(form);
        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Twilio send failed for attempt {AttemptId}. Status {StatusCode}. Response: {Response}",
                notification.NotificationAttemptId,
                (int)response.StatusCode,
                responseText);
            return new GatewayResult(false, null, $"Twilio send failed: {(int)response.StatusCode}");
        }

        var sid = TryExtractJsonValue(responseText, "sid");
        return new GatewayResult(false, sid, "Sent to Twilio.");
    }

    private static string? BuildCallbackUrl(string? baseUrl, Guid notificationAttemptId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}attemptId={notificationAttemptId:D}";
    }

    private static string? TryExtractJsonValue(string json, string fieldName)
    {
        var marker = $"\"{fieldName}\":\"";
        var markerIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = json.IndexOf('"', valueStart);
        if (valueEnd < 0)
        {
            return null;
        }

        return json[valueStart..valueEnd];
    }
}
