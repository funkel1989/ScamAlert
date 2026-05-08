using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Core.Tests.CloudAlerts;

public sealed class CloudAlertRetryScheduleTests
{
    [Fact]
    public void ComputeNextAttemptUtc_doubles_until_capped()
    {
        var t0 = DateTimeOffset.Parse("2026-05-08T12:00:00Z");
        var initial = TimeSpan.FromSeconds(2);
        var max = TimeSpan.FromSeconds(10);

        var n1 = CloudAlertRetrySchedule.ComputeNextAttemptUtc(1, t0, initial, max);
        Assert.Equal(t0 + TimeSpan.FromSeconds(2), n1);

        var n2 = CloudAlertRetrySchedule.ComputeNextAttemptUtc(2, t0, initial, max);
        Assert.Equal(t0 + TimeSpan.FromSeconds(4), n2);

        var n5 = CloudAlertRetrySchedule.ComputeNextAttemptUtc(5, t0, initial, max);
        Assert.Equal(t0 + TimeSpan.FromSeconds(10), n5);
    }

    [Fact]
    public void WithJitter_is_within_span()
    {
        var utc = DateTimeOffset.Parse("2026-05-08T12:00:00Z");
        var r = new Random(42);
        var jittered = CloudAlertRetrySchedule.WithJitter(utc, TimeSpan.FromSeconds(5), r);
        Assert.True(jittered >= utc);
        Assert.True(jittered <= utc + TimeSpan.FromSeconds(5));
    }
}
