using ScamAlert.Contracts;

namespace ScamAlert.Core.Broker;

// In-process short-TTL cache that lets the broker collapse a burst of
// classify events for the same flow into a single user prompt.
//
// TCP retransmits a SYN ~3s, ~9s, ~21s after the original; each retransmit
// hits our WFP layer as a fresh ALE_AUTH_RECV_ACCEPT classify, which the
// broker would otherwise show as a separate prompt. With this cache, the
// first attempt for a given (sourceIp, destinationPort) reaches the user;
// subsequent attempts within the TTL re-use the same verdict without
// prompting again.
//
// Only the verdict shape is cached - never an EventId, since each call
// must reply with the EventId of THE attempt being decided.
public interface IRecentDecisionCache
{
    // Looks up a recent verdict for (sourceIp, destinationPort). Returns
    // false if no cached entry exists or if the entry has aged out of TTL.
    bool TryGet(string sourceIp, int destinationPort, out CachedDecision decision);

    // Stores the verdict for (sourceIp, destinationPort). Overwrites any
    // existing entry; the newest decision wins.
    void Set(string sourceIp, int destinationPort, CachedDecision decision);
}

public readonly record struct CachedDecision(DriverDecisionKind Decision, string Reason);
