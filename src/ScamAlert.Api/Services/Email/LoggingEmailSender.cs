namespace ScamAlert.Api.Services.Email;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email (logging only) To={To} Subject={Subject} Body={Body}",
            toAddress,
            subject,
            plainTextBody);
        return Task.CompletedTask;
    }
}
