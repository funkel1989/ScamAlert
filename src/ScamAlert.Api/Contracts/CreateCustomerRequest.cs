namespace ScamAlert.Api.Contracts;

public sealed record CreateCustomerRequest(
    string Name,
    string? Email,
    string PlanCode,
    List<CreateContactRequest> Contacts,
    List<CreateDeviceRequest> Devices);

public sealed record CreateContactRequest(
    string FullName,
    string PhoneNumber,
    int EscalationOrder);

public sealed record CreateDeviceRequest(
    string DeviceName,
    string ExternalDeviceId);
