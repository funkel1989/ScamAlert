using ScamAlert.Contracts;

namespace ScamAlert.Core.Configuration;

public sealed record ProtectionSettings(TimeoutPolicy TimeoutPolicy, int PromptTimeoutSeconds)
{
    public static ProtectionSettings Default { get; } = new(TimeoutPolicy.AllowOnTimeout, 10);
}
