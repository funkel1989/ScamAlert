using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Services.Audit;
using ScamAlert.Api.Services.Notifications;
using ScamAlert.Data;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Controllers;

[Route("api/webhooks/twilio")]
[AllowAnonymous]
public sealed class TwilioWebhooksController(
    ScamAlertDbContext dbContext,
    ITwilioRequestValidator twilioRequestValidator,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpPost("status")]
    public async Task<IActionResult> MessageStatus([FromQuery] Guid? attemptId, CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        if (!twilioRequestValidator.IsValid(Request, form))
        {
            auditLogger.WebhookRejected("twilio", "status", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Unauthorized();
        }

        if (attemptId is null)
        {
            return Ok();
        }

        var attempt = await dbContext.NotificationAttempts
            .SingleOrDefaultAsync(x => x.Id == attemptId.Value, cancellationToken);

        if (attempt is null)
        {
            return NotFound();
        }

        var messageSid = form["MessageSid"].ToString();
        var messageStatus = form["MessageStatus"].ToString();
        var errorCode = form["ErrorCode"].ToString();

        attempt.ProviderMessageId = string.IsNullOrWhiteSpace(messageSid)
            ? attempt.ProviderMessageId
            : messageSid;

        if (!string.IsNullOrWhiteSpace(messageStatus) &&
            (messageStatus.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
             messageStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase)))
        {
            attempt.Outcome = NotificationOutcome.Failed;
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            attempt.Notes = $"Twilio error code: {errorCode}";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("inbound-sms")]
    public async Task<IActionResult> InboundSms(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        if (!twilioRequestValidator.IsValid(Request, form))
        {
            auditLogger.WebhookRejected("twilio", "inbound-sms", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Unauthorized();
        }

        var body = form["Body"].ToString();
        var from = form["From"].ToString();
        var ackToken = ParseAckToken(body);

        if (string.IsNullOrWhiteSpace(ackToken))
        {
            return Content("<Response/>", "application/xml");
        }

        var attempt = await dbContext.NotificationAttempts
            .Include(x => x.Contact)
            .Include(x => x.AlertEvent)
            .OrderByDescending(x => x.AttemptedUtc)
            .FirstOrDefaultAsync(
                x => x.AcknowledgmentToken == ackToken &&
                     x.Contact.PhoneNumber == from,
                cancellationToken);

        if (attempt is null)
        {
            return Content("<Response/>", "application/xml");
        }

        attempt.Outcome = NotificationOutcome.Acknowledged;
        attempt.AcknowledgedUtc = DateTimeOffset.UtcNow;
        attempt.AlertEvent.AcknowledgedByContactId = attempt.ContactId;
        attempt.AlertEvent.ResolutionStatus = AlertResolutionStatus.Acknowledged;
        attempt.AlertEvent.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Content("<Response><Message>Thanks, alert acknowledged.</Message></Response>", "application/xml");
    }

    private static string? ParseAckToken(string body)
    {
        var match = Regex.Match(body ?? string.Empty, @"\bACK\s+([A-Z0-9]{4,12})\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }
}
