namespace ScamAlert.Core.Broker;

public sealed class InMemoryRecentDecisionCache : IRecentDecisionCache
{
    // 30s comfortably covers Windows TCP's full SYN retransmit budget
    // (typically ~3s, ~9s, ~21s before the client gives up). Combined with
    // sliding TTL refresh on hits, a sustained burst of retries from the
    // same source coalesces into a single user prompt; once the retries
    // stop, the entry expires naturally.
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly TimeSpan ttl;
    private readonly TimeProvider clock;
    private readonly Dictionary<(string SourceIp, int DestinationPort), Entry> entries = new();
    private readonly Lock gate = new();

    public InMemoryRecentDecisionCache()
        : this(DefaultTtl, TimeProvider.System) { }

    public InMemoryRecentDecisionCache(TimeSpan ttl, TimeProvider? clock = null)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL must be positive.");
        }
        this.ttl = ttl;
        this.clock = clock ?? TimeProvider.System;
    }

    public bool TryGet(string sourceIp, int destinationPort, out CachedDecision decision)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceIp);

        var now = clock.GetUtcNow();
        lock (gate)
        {
            var key = (sourceIp, destinationPort);
            if (entries.TryGetValue(key, out var entry))
            {
                if (now - entry.SetAt <= ttl)
                {
                    // Sliding TTL: extend the window on every hit so a
                    // burst of retransmits keeps the entry alive. The
                    // entry only expires once retries actually stop.
                    entries[key] = entry with { SetAt = now };
                    decision = entry.Decision;
                    return true;
                }
                entries.Remove(key);
            }
        }

        decision = default;
        return false;
    }

    public void Set(string sourceIp, int destinationPort, CachedDecision decision)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceIp);

        var now = clock.GetUtcNow();
        lock (gate)
        {
            entries[(sourceIp, destinationPort)] = new Entry(decision, now);

            // Cheap opportunistic cleanup so the dictionary doesn't grow
            // unbounded under attack-style traffic (lots of unique source
            // IPs). N is small in practice; full O(n) sweep is fine.
            if (entries.Count > 64)
            {
                var stale = new List<(string, int)>();
                foreach (var kvp in entries)
                {
                    if (now - kvp.Value.SetAt > ttl)
                    {
                        stale.Add(kvp.Key);
                    }
                }
                foreach (var key in stale)
                {
                    entries.Remove(key);
                }
            }
        }
    }

    private readonly record struct Entry(CachedDecision Decision, DateTimeOffset SetAt);
}
