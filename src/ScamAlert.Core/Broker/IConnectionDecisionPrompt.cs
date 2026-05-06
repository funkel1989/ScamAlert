using ScamAlert.Contracts;

namespace ScamAlert.Core.Broker;

public interface IConnectionDecisionPrompt
{
    Task<DecisionPromptResponse?> RequestDecisionAsync(
        DecisionPromptRequest request,
        CancellationToken cancellationToken);
}
