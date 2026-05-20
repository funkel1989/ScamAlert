namespace ScamAlert.Configurator;

internal sealed record PairingRedeemResult(
    string ApiBaseUrl,
    string ExternalDeviceId,
    string DeviceIngestApiKey,
    string DeviceName);
