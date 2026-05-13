using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Stripe;
using ScamAlert.Api.Services.Web;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;
using Stripe.Checkout;

namespace ScamAlert.Api.Services.Signup;

public interface ISignupService
{
    Task<SignupResult> RegisterAndStartCheckoutAsync(SelfServeSignupRequest request, CancellationToken cancellationToken);
}

public sealed record SignupResult(Guid CustomerId, string CheckoutUrl);

public sealed class SignupService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IOptions<StripeOptions> stripeOptions,
    IOptions<WebSiteOptions> webOptions,
    IOptions<BillingOptions> billingOptions,
    IBillingTierCatalog billingTierCatalog) : ISignupService
{
    public async Task<SignupResult> RegisterAndStartCheckoutAsync(SelfServeSignupRequest request, CancellationToken cancellationToken)
    {
        if (request.Contacts.Count == 0)
        {
            throw new ArgumentException("At least one contact is required.", nameof(request));
        }

        if (request.Contacts.GroupBy(x => x.EscalationOrder).Any(x => x.Count() > 1))
        {
            throw new ArgumentException("Contacts must have distinct escalation order values.", nameof(request));
        }

        if (request.Devices.Count == 0)
        {
            throw new ArgumentException("At least one monitored device is required.", nameof(request));
        }

        var email = request.Email.Trim();
        if (email.Length is < 3 or > 320)
        {
            throw new ArgumentException("Invalid email.", nameof(request));
        }

        if (request.Password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.", nameof(request));
        }

        var emailTaken = await dbContext.AuthUserCredentials.AnyAsync(x => x.Username == email, cancellationToken)
            || await dbContext.Customers.AnyAsync(x => x.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new InvalidOperationException("An account with this email already exists.");
        }

        var stripe = stripeOptions.Value;
        var web = webOptions.Value;
        var billing = billingOptions.Value;
        var now = DateTimeOffset.UtcNow;
        var customerId = Guid.NewGuid();
        var deviceProvisioning = request.Devices
            .Select(x => new { Request = x, ApiKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) })
            .ToList();

        var customer = new Customer
        {
            Id = customerId,
            Name = request.Name.Trim(),
            Email = email,
            CreatedUtc = now,
            UpdatedUtc = now,
            Subscriptions =
            [
                new Subscription
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    PlanCode = request.PlanCode.Trim(),
                    Status = stripe.SkipPaymentForDevelopment ? SubscriptionStatus.Active : SubscriptionStatus.PendingPayment,
                    StartsUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }
            ],
            Contacts = request.Contacts.Select(x => new Contact
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                FullName = x.FullName.Trim(),
                PhoneNumber = x.PhoneNumber.Trim(),
                EscalationOrder = x.EscalationOrder,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList(),
            Devices = deviceProvisioning.Select(x => new MonitoredDevice
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                DeviceName = x.Request.DeviceName.Trim(),
                ExternalDeviceId = x.Request.ExternalDeviceId.Trim(),
                IngestApiKeyHash = passwordHasher.HashPassword(x.ApiKey),
                IngestApiKeyCreatedUtc = now,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList()
        };

        dbContext.Customers.Add(customer);
        dbContext.AuthUserCredentials.Add(new AuthUserCredential
        {
            Id = Guid.NewGuid(),
            Username = email,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            RolesCsv = "operator",
            CustomerScopeCsv = customerId.ToString("D"),
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (stripe.SkipPaymentForDevelopment)
        {
            return new SignupResult(customerId, $"{TrimSlash(web.PublicBaseUrl)}/signup/success");
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            throw new InvalidOperationException(
                "Stripe is not configured. Set Stripe:SecretKey and Billing:Tiers, or enable Stripe:SkipPaymentForDevelopment for local testing.");
        }

        var stripePriceId = billingTierCatalog.GetStripePriceId(request.PlanCode);
        global::Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;
        var baseUrl = TrimSlash(web.PublicBaseUrl);
        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(
            new SessionCreateOptions
            {
                Mode = "subscription",
                ClientReferenceId = customerId.ToString("D"),
                CustomerEmail = email,
                Locale = string.IsNullOrWhiteSpace(billing.Locale) ? null : billing.Locale.Trim(),
                BillingAddressCollection = billing.RequireBillingAddressOnCheckout ? "required" : "auto",
                LineItems =
                [
                    new SessionLineItemOptions { Price = stripePriceId, Quantity = 1 }
                ],
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["plan_code"] = request.PlanCode.Trim(),
                        ["app_customer_id"] = customerId.ToString("D")
                    }
                },
                SuccessUrl = $"{baseUrl}/signup/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{baseUrl}/signup",
                Metadata = new Dictionary<string, string>
                {
                    ["customerId"] = customerId.ToString("D"),
                    ["plan_code"] = request.PlanCode.Trim()
                }
            },
            cancellationToken: cancellationToken);

        return new SignupResult(customerId, session.Url ?? throw new InvalidOperationException("Stripe returned no checkout URL."));
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');
}
