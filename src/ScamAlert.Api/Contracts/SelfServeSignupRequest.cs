namespace ScamAlert.Api.Contracts;

public sealed record SelfServeSignupRequest(
    string Name,
    string Email,
    string Password,
    string PlanCode,
    List<CreateContactRequest> Contacts,
    List<CreateDeviceRequest> Devices);
