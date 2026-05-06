using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScamAlert.Contracts;

public enum ProtectedService
{
    Rdp,
    Ssh,
    Telnet
}

public enum TimeoutPolicy
{
    AllowOnTimeout,
    BlockOnTimeout
}

public enum DecisionStatus
{
    Pending,
    Remembered,
    UserSelected,
    TimedOut
}

public enum UserDecisionKind
{
    AllowOnce,
    BlockOnce
}

public enum DriverDecisionKind
{
    Allow,
    Block
}

public static class ProtectedServiceMap
{
    public static bool TryFromPort(int destinationPort, out ProtectedService service)
    {
        service = destinationPort switch
        {
            3389 => ProtectedService.Rdp,
            22 => ProtectedService.Ssh,
            23 => ProtectedService.Telnet,
            _ => default
        };

        return destinationPort is 3389 or 22 or 23;
    }
}

public sealed record ProtectedConnectionAttempt(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService);

public sealed record ObservedInboundAttemptSignal(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService,
    TimeoutPolicy LocalPolicyMode,
    DecisionStatus DecisionStatus)
{
    public string EventType => "ObservedInboundAttempt";
}

public sealed record UserDecisionUpdatedSignal(
    Guid EventId,
    Guid ObservedEventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    UserDecisionKind Decision,
    bool Remembered,
    string Reason)
{
    public string EventType => "UserDecisionUpdated";
}

public sealed record DecisionPromptRequest(
    Guid ObservedEventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService,
    TimeoutPolicy LocalPolicyMode,
    int TimeoutSeconds);

public sealed record DecisionPromptResponse(
    Guid ObservedEventId,
    UserDecisionKind Decision,
    bool Remember);

public sealed record DriverDecisionResponse(
    Guid ObservedEventId,
    DriverDecisionKind Decision,
    string Reason);

public static class SignalJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
