using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScamAlert.Api.Services.Email;

public sealed class SendGridEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options,
    ILogger<SendGridEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken)
    {
        var email = options.Value;
        var apiKey = email.SendGridApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("SendGrid API key is not configured.");
        }

        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = toAddress } } } },
            from = new { email = email.FromAddress, name = email.FromDisplayName },
            subject,
            content = new[] { new { type = "text/plain", value = plainTextBody } }
        };

        var client = httpClientFactory.CreateClient(nameof(SendGridEmailSender));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("SendGrid failed {Status}: {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }
}
