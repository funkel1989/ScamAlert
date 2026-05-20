using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Pairing;

public interface IDevicePairingService
{
    Task<DevicePairingCodeResponse?> CreateCodeAsync(
        Guid customerId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<DevicePairingRedeemResponse?> RedeemAsync(string code, CancellationToken cancellationToken);
}
