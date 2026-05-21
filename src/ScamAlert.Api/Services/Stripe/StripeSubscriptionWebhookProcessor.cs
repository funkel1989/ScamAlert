using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Signup;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;
using Stripe;
using Stripe.Checkout;

namespace ScamAlert.Api.Services.Stripe;

public interface IStripeSubscriptionWebhookProcessor
{
    Task ProcessAsync(Event stripeEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Applies Stripe subscription lifecycle events to <see cref="ScamAlert.Data.Entities.Subscription"/> (recurring billing).
/// </summary>
public sealed class StripeSubscriptionWebhookProcessor(
    ScamAlertDbContext dbContext,
    IOptions<StripeOptions> stripeOptions,
    IBillingTierCatalog billingTierCatalog,
    ISubscriptionPaymentActivator legacyActivator,
    ILogger<StripeSubscriptionWebhookProcessor> logger) : IStripeSubscriptionWebhookProcessor
{
    public async Task ProcessAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        ApplyApiKey();

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed" when stripeEvent.Data.Object is Session session:
                await HandleCheckoutSessionCompletedAsync(session, cancellationToken);
                break;
            case "customer.subscription.updated" when stripeEvent.Data.Object is global::Stripe.Subscription stripeSub:
                await ApplyStripeSubscriptionAsync(stripeSub, cancellationToken);
                break;
            case "customer.subscription.deleted" when stripeEvent.Data.Object is global::Stripe.Subscription deleted:
                await HandleSubscriptionDeletedAsync(deleted.Id, cancellationToken);
                break;
            case "invoice.paid" when stripeEvent.Data.Object is Invoice paid:
                await HandleInvoicePaidAsync(paid, cancellationToken);
                break;
            case "invoice.payment_failed" when stripeEvent.Data.Object is Invoice failed:
                await HandleInvoicePaymentFailedAsync(failed, cancellationToken);
                break;
            case "customer.updated" when stripeEvent.Data.Object is global::Stripe.Customer stripeCustomer:
                await SyncBillingAddressAsync(stripeCustomer, cancellationToken);
                break;
            default:
                logger.LogDebug("Ignoring Stripe event type {Type}.", stripeEvent.Type);
                break;
        }
    }

    private void ApplyApiKey()
    {
        var key = stripeOptions.Value.SecretKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            global::Stripe.StripeConfiguration.ApiKey = key;
        }
    }

    private async Task HandleCheckoutSessionCompletedAsync(Session session, CancellationToken cancellationToken)
    {
        if (!TryResolveCustomerId(session, out var customerId))
        {
            logger.LogWarning("Checkout session {SessionId} missing customer reference.", session.Id);
            return;
        }

        if (session.Mode == "subscription" && !string.IsNullOrEmpty(session.SubscriptionId))
        {
            var stripeSub = await new global::Stripe.SubscriptionService().GetAsync(
                session.SubscriptionId,
                cancellationToken: cancellationToken);

            var customer = await dbContext.Customers
                .Include(x => x.Subscriptions)
                .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);

            if (customer is null)
            {
                logger.LogWarning("Customer {CustomerId} not found for Stripe checkout.", customerId);
                return;
            }

            if (!string.IsNullOrEmpty(session.CustomerId))
            {
                customer.StripeCustomerId = session.CustomerId;
                customer.UpdatedUtc = DateTimeOffset.UtcNow;
                await StripeBillingAddressMapper.TrySyncFromStripeCustomerAsync(
                    customer,
                    session.CustomerId,
                    cancellationToken);
            }

            var localSub = customer.Subscriptions
                .Where(x => x.Status == SubscriptionStatus.PendingPayment)
                .OrderByDescending(x => x.StartsUtc)
                .FirstOrDefault();

            if (localSub is null)
            {
                logger.LogWarning("No pending subscription for customer {CustomerId} at checkout completion.", customerId);
                return;
            }

            StripeSubscriptionMapper.MapToLocal(localSub, stripeSub, billingTierCatalog);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (session.Mode == "payment")
        {
            await legacyActivator.TryActivateCustomerAsync(customerId, cancellationToken);
        }
    }

    private async Task ApplyStripeSubscriptionAsync(global::Stripe.Subscription stripeSub, CancellationToken cancellationToken)
    {
        var local = await dbContext.Subscriptions
            .Include(x => x.Customer)
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == stripeSub.Id, cancellationToken);

        if (local is null)
        {
            logger.LogWarning("No local subscription for Stripe subscription {StripeId}.", stripeSub.Id);
            return;
        }

        StripeSubscriptionMapper.MapToLocal(local, stripeSub, billingTierCatalog);
        local.Customer.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        var local = await dbContext.Subscriptions
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscriptionId, cancellationToken);

        if (local is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        local.Status = SubscriptionStatus.Canceled;
        local.EndsUtc = now;
        local.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshSubscriptionFromStripeAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        var stripeSub = await new global::Stripe.SubscriptionService().GetAsync(
            stripeSubscriptionId,
            cancellationToken: cancellationToken);
        await ApplyStripeSubscriptionAsync(stripeSub, cancellationToken);
    }

    private async Task HandleInvoicePaidAsync(Invoice paid, CancellationToken cancellationToken)
    {
        var stripeSubscriptionId = TryGetInvoiceSubscriptionId(paid);
        if (string.IsNullOrEmpty(stripeSubscriptionId))
        {
            return;
        }

        await RefreshSubscriptionFromStripeAsync(stripeSubscriptionId, cancellationToken);
        await ApplyInvoicePeriodEndAsync(paid, stripeSubscriptionId, cancellationToken);
    }

    private async Task HandleInvoicePaymentFailedAsync(Invoice failed, CancellationToken cancellationToken)
    {
        var stripeSubscriptionId = TryGetInvoiceSubscriptionId(failed);
        if (string.IsNullOrEmpty(stripeSubscriptionId))
        {
            return;
        }

        await HandlePaymentFailedAsync(stripeSubscriptionId, cancellationToken);
    }

    private async Task ApplyInvoicePeriodEndAsync(
        Invoice paid,
        string stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        var local = await dbContext.Subscriptions
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscriptionId, cancellationToken);

        if (local is null || paid.PeriodEnd == default)
        {
            return;
        }

        var end = paid.PeriodEnd.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(paid.PeriodEnd)
            : new DateTimeOffset(DateTime.SpecifyKind(paid.PeriodEnd, DateTimeKind.Utc));
        local.EndsUtc = end;
        local.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task HandlePaymentFailedAsync(string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        var local = await dbContext.Subscriptions
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscriptionId, cancellationToken);

        if (local is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        local.Status = SubscriptionStatus.PastDue;
        local.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? TryGetInvoiceSubscriptionId(Invoice invoice) =>
        invoice.Parent?.SubscriptionDetails?.SubscriptionId;

    private async Task SyncBillingAddressAsync(global::Stripe.Customer stripeCustomer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomer.Id))
        {
            return;
        }

        var customer = await dbContext.Customers
            .SingleOrDefaultAsync(x => x.StripeCustomerId == stripeCustomer.Id, cancellationToken);

        if (customer is null)
        {
            return;
        }

        if (StripeBillingAddressMapper.TryApplyFromStripeAddress(
                customer,
                stripeCustomer.Address,
                DateTimeOffset.UtcNow))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
