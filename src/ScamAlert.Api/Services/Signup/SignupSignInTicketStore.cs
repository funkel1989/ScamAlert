using System.Collections.Concurrent;

namespace ScamAlert.Api.Services.Signup;

public interface ISignupSignInTicketStore
{
    string Create(Guid customerId);

    bool TryConsume(string ticket, out Guid customerId);
}

/// <summary>
/// Short-lived, one-time tickets so post-signup sign-in runs on a fresh HTTP request (not during Blazor render).
/// </summary>
public sealed class SignupSignInTicketStore : ISignupSignInTicketStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (Guid CustomerId, DateTimeOffset ExpiresUtc)> _tickets = new(StringComparer.Ordinal);

    public string Create(Guid customerId)
    {
        PurgeExpired();
        var ticket = Guid.NewGuid().ToString("N");
        _tickets[ticket] = (customerId, DateTimeOffset.UtcNow.Add(Lifetime));
        return ticket;
    }

    public bool TryConsume(string ticket, out Guid customerId)
    {
        customerId = default;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return false;
        }

        PurgeExpired();
        if (!_tickets.TryRemove(ticket.Trim(), out var entry))
        {
            return false;
        }

        if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        customerId = entry.CustomerId;
        return true;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _tickets.Keys)
        {
            if (_tickets.TryGetValue(key, out var entry) && entry.ExpiresUtc <= now)
            {
                _tickets.TryRemove(key, out _);
            }
        }
    }
}
