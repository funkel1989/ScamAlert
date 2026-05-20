using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Contracts;

public sealed record PortalDeviceResponse(
    Guid Id,
    string DeviceName,
    string ExternalDeviceId,
    bool IsActive,
    bool HasIngestKey,
    DateTimeOffset? IngestApiKeyCreatedUtc,
    DateTimeOffset CreatedUtc);

public sealed record PortalDeviceCreatedResponse(
    Guid Id,
    string DeviceName,
    string ExternalDeviceId,
    string IngestApiKey);

public sealed record CreatePortalDeviceRequest(
    string DeviceName,
    string? ExternalDeviceId);

public sealed record PortalContactResponse(
    Guid Id,
    string FullName,
    string PhoneNumber,
    int EscalationOrder,
    bool IsActive);

public sealed record CreatePortalContactRequest(
    string FullName,
    string PhoneNumber,
    int EscalationOrder);

public sealed record UpdatePortalContactRequest(
    string FullName,
    string PhoneNumber,
    int EscalationOrder,
    bool IsActive);

public sealed record SignupPlanResponse(string PlanCode, string DisplayName);

public sealed record ProvisionedDeviceResponse(
    string DeviceName,
    string ExternalDeviceId,
    string IngestApiKey);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record DevicePairingCodeResponse(string Code, DateTimeOffset ExpiresUtc);

public sealed record DevicePairingRedeemRequest(string Code);

public sealed record DevicePairingRedeemResponse(
    string ApiBaseUrl,
    string ExternalDeviceId,
    string DeviceIngestApiKey,
    string DeviceName);
