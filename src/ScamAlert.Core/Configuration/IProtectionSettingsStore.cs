namespace ScamAlert.Core.Configuration;

public interface IProtectionSettingsStore
{
    Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken);
}
