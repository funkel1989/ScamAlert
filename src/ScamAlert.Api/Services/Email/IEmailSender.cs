namespace ScamAlert.Api.Services.Email;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken);
}
