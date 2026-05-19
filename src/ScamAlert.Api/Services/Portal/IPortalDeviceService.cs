using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Portal;

public interface IPortalDeviceService
{
    Task<IReadOnlyList<PortalDeviceResponse>> ListAsync(Guid customerId, CancellationToken cancellationToken);

    Task<PortalDeviceCreatedResponse> CreateAsync(
        Guid customerId,
        CreatePortalDeviceRequest request,
        CancellationToken cancellationToken);

    Task<PortalDeviceCreatedResponse?> RotateIngestKeyAsync(
        Guid customerId,
        Guid deviceId,
        CancellationToken cancellationToken);
}
