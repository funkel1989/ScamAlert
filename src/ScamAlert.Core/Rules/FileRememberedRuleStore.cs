using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Rules;

public sealed class FileRememberedRuleStore : IRememberedRuleStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public FileRememberedRuleStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.path = path;
    }

    public async Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIp);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var rules = await ReadRulesAsync(cancellationToken);
            return rules.FirstOrDefault(rule => string.Equals(rule.SourceIp, sourceIp, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.SourceIp);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var rules = await ReadRulesAsync(cancellationToken);
            var existingIndex = rules.FindIndex(existing =>
                string.Equals(existing.SourceIp, rule.SourceIp, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                rules[existingIndex] = rule;
            }
            else
            {
                rules.Add(rule);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(rules, SignalJson.Options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<RememberedIpRule>> ReadRulesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<List<RememberedIpRule>>(json, SignalJson.Options) ?? [];
    }
}
