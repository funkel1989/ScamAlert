using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Billing;
using ScamAlert.Api.Services.Email;
using ScamAlert.Api.Services.Phone;
using ScamAlert.Api.Services.Validation;
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

public sealed record SignupResult(
    Guid CustomerId,
    string CheckoutUrl,
    string? SignInTicket,
    IReadOnlyList<ProvisionedDeviceResponse> ProvisionedDevices);

public sealed class SignupService(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IEmailVerificationService emailVerificationService,
    IOptions<StripeOptions> stripeOptions,
    IOptions<WebSiteOptions> webOptions,
    IOptions<BillingOptions> billingOptions,
    IBillingTierCatalog billingTierCatalog,
    ISignupSignInTicketStore signInTicketStore,
    ILogger<SignupService> logger) : ISignupService
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

        if (!EmailAddressValidator.TryValidate(request.Email, out var email, out var emailError))
        {
            throw new ArgumentException(emailError, nameof(request));
        }

        if (!PasswordPolicy.TryValidate(request.Password, out var passwordError))
        {
            throw new ArgumentException(passwordError, nameof(request));
        }

        if (!request.Consents.AcceptTerms
            || !request.Consents.AcceptPrivacy
            || !request.Consents.AcceptSmsConsent
            || !request.Consents.ConfirmInstallPermission)
        {
            throw new ArgumentException("All agreements must be accepted before signup.", nameof(request));
        }

        var normalizedContacts = new List<(CreateContactRequest Request, string E164)>();
        foreach (var contact in request.Contacts)
        {
            if (!UsPhoneNumber.TryNormalize(contact.PhoneNumber, out var e164, out var phoneError))
            {
                throw new ArgumentException(phoneError, nameof(request));
            }

            normalizedContacts.Add((contact, e164));
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

        var consentIp = string.IsNullOrWhiteSpace(request.ConsentIpAddress)
            ? null
            : request.ConsentIpAddress.Trim()[..Math.Min(request.ConsentIpAddress.Trim().Length, 64)];

        var customer = new Customer
        {
            Id = customerId,
            Name = request.Name.Trim(),
            Email = email,
            TermsAcceptedUtc = now,
            PrivacyAcceptedUtc = now,
            SmsConsentAcceptedUtc = now,
            InstallPermissionConfirmedUtc = now,
            SignupConsentIpAddress = consentIp,
            SignupLegalDocumentVersion = string.IsNullOrWhiteSpace(request.Consents.LegalDocumentVersion)
                ? null
                : request.Consents.LegalDocumentVersion.Trim()[..Math.Min(request.Consents.LegalDocumentVersion.Trim().Length, 20)],
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
            Contacts = normalizedContacts.Select(x => new Contact
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                FullName = x.Request.FullName.Trim(),
                PhoneNumber = x.E164,
                EscalationOrder = x.Request.EscalationOrder,
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

        var provisionedDevices = deviceProvisioning
            .Select(x => new ProvisionedDeviceResponse(
                x.Request.DeviceName.Trim(),
                x.Request.ExternalDeviceId.Trim(),
                x.ApiKey))
            .ToList();

        await emailVerificationService.SendVerificationEmailAsync(email, cancellationToken);
        await TrySendWelcomeEmailAsync(email, request.Name.Trim(), provisionedDevices, web.PublicBaseUrl, cancellationToken);

        if (stripe.SkipPaymentForDevelopment)
        {
            var ticket = signInTicketStore.Create(customerId);
            return new SignupResult(
                customerId,
                "/signup/complete",
                ticket,
                provisionedDevices);
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
                SuccessUrl = $"{baseUrl}/signup/complete?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{baseUrl}/signup",
                Metadata = new Dictionary<string, string>
                {
                    ["customerId"] = customerId.ToString("D"),
                    ["plan_code"] = request.PlanCode.Trim()
                }
            },
            cancellationToken: cancellationToken);

        return new SignupResult(
            customerId,
            session.Url ?? throw new InvalidOperationException("Stripe returned no checkout URL."),
            SignInTicket: null,
            provisionedDevices);
    }

    private async Task TrySendWelcomeEmailAsync(
        string email,
        string name,
        IReadOnlyList<ProvisionedDeviceResponse> devices,
        string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        var deviceLines = string.Join(
            Environment.NewLine,
            devices.Select(d =>
                $"- {d.DeviceName}: external id {d.ExternalDeviceId}, ingest key (save now): {d.IngestApiKey}"));

        var body = $"""
            Hi {name},

            Welcome to ScamAlert. Sign in at {TrimSlash(publicBaseUrl)}/login to manage contacts and billing.

            Your protected device credentials (shown once):
            {deviceLines}

            Configure the Windows broker CloudAlerts settings with these values, or add more devices from the portal after sign-in.
            """;

        try
        {
            await emailSender.SendAsync(email, "Welcome to ScamAlert", body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Welcome email failed for {Email}", email);
        }
    }

    private static string TrimSlash(string url) => url.TrimEnd('/');
}
