using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Stripe;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using ScamAlert.Data.Enums;
using Stripe;
using Stripe.BillingPortal;

namespace ScamAlert.Api.Services.Billing;

public sealed class CustomerBillingService(
    ScamAlertDbContext dbContext,
    ICurrentUserAccessService access,
    IAuthorizationService authorizationService,
    IOptions<StripeOptions> stripeOptions,
    IOptions<WebSiteOptions> webOptions,
    IOptions<BillingOptions> billingOptions,
    IBillingTierCatalog billingTierCatalog) : ICustomerBillingService
{
    public async Task<BillingSummaryResponse?> GetSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var customerId = await TryResolveAuthorizedCustomerIdAsync(user, cancellationToken);
        if (customerId is not { } resolvedId)
        {
            return null;
        }

        var stripe = stripeOptions.Value;
        var billing = billingOptions.Value;
        var customer = await dbContext.Customers.AsNoTracking()
            .Include(c => c.Subscriptions)
            .SingleAsync(c => c.Id == resolvedId, cancellationToken);

        var sub = customer.Subscriptions.OrderByDescending(s => s.StartsUtc).FirstOrDefault();
        var tiers = billing.Tiers
            .Select(t => new BillingTierSummaryDto(
                t.PlanCode,
                string.IsNullOrWhiteSpace(t.DisplayName) ? t.PlanCode : t.DisplayName.Trim()))
            .ToList();

        var portalReason = ResolvePortalUnavailableReason(customer, stripe);
        return new BillingSummaryResponse(
            customer.Id,
            customer.StripeCustomerId,
            sub?.PlanCode ?? string.Empty,
            sub?.Status.ToString() ?? nameof(SubscriptionStatus.Canceled),
            sub?.EndsUtc,
            portalReason is null,
            portalReason,
            tiers,
            billing.MarketRegion.Trim(),
            StripeBillingAddressMapper.ToDto(customer),
            customer.BillingAddressSyncedUtc);
    }

    public async Task UpdateBillingAddressAsync(
        ClaimsPrincipal user,
        UpdateBillingAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (!BillingAddressValidator.TryValidate(request, out var validationError))
        {
            throw new ArgumentException(validationError);
        }

        var customerId = await TryResolveAuthorizedCustomerIdAsync(user, cancellationToken)
            ?? throw new InvalidOperationException("Billing address can only be updated for a single-organization account.");

        var stripe = stripeOptions.Value;
        var customer = await dbContext.Customers
            .SingleAsync(c => c.Id == customerId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        StripeBillingAddressMapper.ApplyToLocalCustomer(customer, request, now);

        if (!stripe.SkipPaymentForDevelopment
            && !string.IsNullOrWhiteSpace(customer.StripeCustomerId)
            && !string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            StripeConfiguration.ApiKey = stripe.SecretKey;
            await StripeBillingAddressMapper.PushToStripeCustomerAsync(
                customer.StripeCustomerId,
                customer,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> CreateCustomerPortalUrlAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var customerId = await TryResolveAuthorizedCustomerIdAsync(user, cancellationToken)
            ?? throw new InvalidOperationException("Billing portal is only available for a single-organization account.");

        var stripe = stripeOptions.Value;
        var web = webOptions.Value;
        var billing = billingOptions.Value;

        if (stripe.SkipPaymentForDevelopment)
        {
            throw new InvalidOperationException("Stripe Customer Portal is disabled while SkipPaymentForDevelopment is enabled.");
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .SingleAsync(c => c.Id == customerId, cancellationToken);

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            throw new InvalidOperationException("No Stripe customer is linked yet. Complete checkout or contact support.");
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            throw new InvalidOperationException("Stripe is not configured.");
        }

        StripeConfiguration.ApiKey = stripe.SecretKey;
        var baseUrl = TrimSlash(web.PublicBaseUrl);
        var returnUrl = $"{baseUrl}/billing";
        var locale = string.IsNullOrWhiteSpace(billing.Locale) ? null : billing.Locale.Trim();

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(
            new SessionCreateOptions
            {
                Customer = customer.StripeCustomerId,
                ReturnUrl = returnUrl,
                Locale = locale
            },
            cancellationToken: cancellationToken);

        return session.Url ?? throw new InvalidOperationException("Stripe returned no portal URL.");
    }

    public async Task ChangePlanAsync(ClaimsPrincipal user, string planCode, CancellationToken cancellationToken)
    {
        var customerId = await TryResolveAuthorizedCustomerIdAsync(user, cancellationToken)
            ?? throw new InvalidOperationException("Plan changes are only available for a single-organization account.");

        var normalized = planCode.Trim();
        _ = billingTierCatalog.GetStripePriceId(normalized);

        var stripe = stripeOptions.Value;
        var customer = await dbContext.Customers
            .Include(c => c.Subscriptions)
            .SingleAsync(c => c.Id == customerId, cancellationToken);

        var sub = customer.Subscriptions.OrderByDescending(s => s.StartsUtc).FirstOrDefault()
            ?? throw new InvalidOperationException("No subscription found.");

        if (!CanChangePlanLocally(sub.Status))
        {
            throw new InvalidOperationException($"Cannot change plan while subscription status is {sub.Status}.");
        }

        if (string.Equals(sub.PlanCode, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (stripe.SkipPaymentForDevelopment)
        {
            sub.PlanCode = normalized;
            sub.UpdatedUtc = DateTimeOffset.UtcNow;
            customer.UpdatedUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(sub.StripeSubscriptionId))
        {
            throw new InvalidOperationException("No Stripe subscription is linked yet. Complete checkout first.");
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            throw new InvalidOperationException("Stripe is not configured.");
        }

        StripeConfiguration.ApiKey = stripe.SecretKey;
        var newPriceId = billingTierCatalog.GetStripePriceId(normalized);
        var stripeSubSvc = new SubscriptionService();
        var stripeSub = await stripeSubSvc.GetAsync(sub.StripeSubscriptionId, cancellationToken: cancellationToken);
        var item = stripeSub.Items?.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException("Stripe subscription has no line items.");

        var mergedMeta = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stripeSub.Metadata is not null)
        {
            foreach (var kv in stripeSub.Metadata)
            {
                mergedMeta[kv.Key] = kv.Value;
            }
        }

        mergedMeta["plan_code"] = normalized;
        mergedMeta["app_customer_id"] = customer.Id.ToString("D");

        await stripeSubSvc.UpdateAsync(
            stripeSub.Id,
            new SubscriptionUpdateOptions
            {
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Id = item.Id,
                        Price = newPriceId,
                        Quantity = 1
                    }
                ],
                ProrationBehavior = "create_prorations",
                Metadata = mergedMeta
            },
            cancellationToken: cancellationToken);

        var updated = await stripeSubSvc.GetAsync(stripeSub.Id, cancellationToken: cancellationToken);
        StripeSubscriptionMapper.MapToLocal(sub, updated, billingTierCatalog);
        customer.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool CanChangePlanLocally(SubscriptionStatus status) =>
        status is SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.PastDue;

    private static string? ResolvePortalUnavailableReason(ScamAlert.Data.Entities.Customer customer, StripeOptions stripe)
    {
        if (stripe.SkipPaymentForDevelopment)
        {
            return "Billing portal is disabled in development (SkipPaymentForDevelopment).";
        }

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            return "Complete Stripe checkout to manage payment methods and invoices.";
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            return "Stripe is not configured on the server.";
        }

        return null;
    }

    private async Task<Guid?> TryResolveAuthorizedCustomerIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (access.HasGlobalAccess(user))
        {
            return null;
        }

        var ids = access.GetAllowedCustomerIds(user);
        if (ids.Count != 1)
        {
            return null;
        }

        var id = ids.Single();
        var auth = await authorizationService.AuthorizeAsync(user, id, AuthPolicies.CustomerScope);
        return auth.Succeeded ? id : null;
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');
}
