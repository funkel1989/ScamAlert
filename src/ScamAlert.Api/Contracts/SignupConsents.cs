namespace ScamAlert.Api.Contracts;

public sealed record SignupConsents(
    bool AcceptTerms,
    bool AcceptPrivacy,
    bool AcceptSmsConsent,
    bool ConfirmInstallPermission,
    string? LegalDocumentVersion = null);
