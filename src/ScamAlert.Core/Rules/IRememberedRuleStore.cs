namespace ScamAlert.Core.Rules;

public interface IRememberedRuleStore
{
    Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken);

    Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken);
}
