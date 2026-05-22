using System.Collections.Concurrent;

namespace ScamAlert.Api.Services.Signup;

public interface IConsumedStripeCheckoutStore
{
    bool TryMarkConsumed(string sessionId);
}

/// <summary>
/// Prevents replay of Stripe Checkout session IDs on the signup completion endpoint.
/// </summary>
public sealed class ConsumedStripeCheckoutStore : IConsumedStripeCheckoutStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);

    public bool TryMarkConsumed(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        PurgeExpired();
        var key = sessionId.Trim();
        var now = DateTimeOffset.UtcNow;
        return _consumed.TryAdd(key, now);
    }

    private void PurgeExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var key in _consumed.Keys)
        {
            if (_consumed.TryGetValue(key, out var at) && at < cutoff)
            {
                _consumed.TryRemove(key, out _);
            }
        }
    }
}
