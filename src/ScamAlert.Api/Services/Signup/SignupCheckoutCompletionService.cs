using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Stripe;
using ScamAlert.Data;
using ScamAlert.Data.Enums;
using Stripe.Checkout;

namespace ScamAlert.Api.Services.Signup;

public interface ISignupCheckoutCompletionService
{
    Task<SignupCheckoutCompletionResult> TryCompleteStripeSessionAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed record SignupCheckoutCompletionResult(bool Success, Guid? CustomerId, string? Error);

public sealed class SignupCheckoutCompletionService(
    ScamAlertDbContext dbContext,
    IOptions<StripeOptions> stripeOptions,
    ISubscriptionPaymentActivator subscriptionPaymentActivator,
    IConsumedStripeCheckoutStore consumedCheckoutStore) : ISignupCheckoutCompletionService
{
    public async Task<SignupCheckoutCompletionResult> TryCompleteStripeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var stripe = stripeOptions.Value;
        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            return new SignupCheckoutCompletionResult(false, null, "Stripe is not configured.");
        }

        global::Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;
        Session session;
        try
        {
            session = await new SessionService().GetAsync(sessionId, cancellationToken: cancellationToken);
        }
        catch (global::Stripe.StripeException)
        {
            return new SignupCheckoutCompletionResult(false, null, "Checkout session not found.");
        }

        if (!string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase))
        {
            return new SignupCheckoutCompletionResult(false, null, "Checkout is not complete yet.");
        }

        if (!string.Equals(session.Mode, "subscription", StringComparison.OrdinalIgnoreCase))
        {
            return new SignupCheckoutCompletionResult(false, null, "Checkout session is not a subscription signup.");
        }

        if (!consumedCheckoutStore.TryMarkConsumed(sessionId))
        {
            return new SignupCheckoutCompletionResult(false, null, "Checkout session was already used.");
        }

        if (!TryResolveCustomerId(session, out var customerId))
        {
            return new SignupCheckoutCompletionResult(false, null, "Checkout session is missing account reference.");
        }

        var customerExists = await dbContext.Customers.AsNoTracking()
            .AnyAsync(x => x.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            return new SignupCheckoutCompletionResult(false, null, "Account not found.");
        }

        var scopeId = customerId.ToString("D");
        var hasPortalLogin = await dbContext.AuthUserCredentials.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.CustomerScopeCsv == scopeId, cancellationToken);
        if (!hasPortalLogin)
        {
            return new SignupCheckoutCompletionResult(false, null, "Account not found.");
        }

        await subscriptionPaymentActivator.TryActivateCustomerAsync(customerId, cancellationToken);
        return new SignupCheckoutCompletionResult(true, customerId, null);
    }

    private static bool TryResolveCustomerId(Session session, out Guid customerId)
    {
        customerId = default;
        if (!string.IsNullOrWhiteSpace(session.ClientReferenceId) &&
            Guid.TryParse(session.ClientReferenceId, out customerId))
        {
            return true;
        }

        if (session.Metadata != null &&
            session.Metadata.TryGetValue("customerId", out var meta) &&
            Guid.TryParse(meta, out customerId))
        {
            return true;
        }

        return false;
    }
}
