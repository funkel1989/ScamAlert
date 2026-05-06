using ScamAlert.Contracts;

namespace ScamAlert.Core.Rules;

public sealed record RememberedIpRule(
    string SourceIp,
    DriverDecisionKind Decision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
