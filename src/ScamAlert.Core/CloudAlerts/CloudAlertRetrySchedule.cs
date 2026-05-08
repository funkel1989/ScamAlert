namespace ScamAlert.Core.CloudAlerts;

public static class CloudAlertRetrySchedule
{
    /// <summary>Computes the next delivery attempt time after a failure, using exponential backoff capped at maxDelay.</summary>
    public static DateTimeOffset ComputeNextAttemptUtc(
        int failureCountOneBased,
        DateTimeOffset referenceUtc,
        TimeSpan initialDelay,
        TimeSpan maxDelay)
    {
        var exponent = Math.Max(0, failureCountOneBased - 1);
        var seconds = Math.Min(
            maxDelay.TotalSeconds,
            initialDelay.TotalSeconds * Math.Pow(2, exponent));

        var delay = TimeSpan.FromSeconds(seconds);
        return referenceUtc + delay;
    }

    /// <summary>Adds small jitter so many clients do not retry in lockstep.</summary>
    public static DateTimeOffset WithJitter(DateTimeOffset utc, TimeSpan span, Random random)
    {
        if (span <= TimeSpan.Zero)
        {
            return utc;
        }

        var jitterMs = random.Next(0, (int)Math.Min(int.MaxValue, span.TotalMilliseconds + 1));
        return utc + TimeSpan.FromMilliseconds(jitterMs);
    }
}
