using ScamAlert.Contracts;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Tests.Rules;

public sealed class FileRememberedRuleStoreTests
{
    [Fact]
    public async Task UpsertAsyncStoresRuleBySourceIp()
    {
        var path = CreatePath();
        var store = new FileRememberedRuleStore(path);
        var rule = new RememberedIpRule(
            SourceIp: "203.0.113.7",
            Decision: DriverDecisionKind.Allow,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:01:00Z"));

        await store.UpsertAsync(rule, CancellationToken.None);

        var saved = await store.FindBySourceIpAsync("203.0.113.7", CancellationToken.None);
        Assert.Equal(rule, saved);
    }

    [Fact]
    public async Task FindBySourceIpAsyncReturnsRuleForSameIp()
    {
        var path = CreatePath();
        var store = new FileRememberedRuleStore(path);
        var rule = new RememberedIpRule(
            SourceIp: "198.51.100.42",
            Decision: DriverDecisionKind.Block,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:01:00Z"));

        await store.UpsertAsync(rule, CancellationToken.None);

        var saved = await store.FindBySourceIpAsync("198.51.100.42", CancellationToken.None);
        Assert.Equal(rule, saved);
    }

    [Fact]
    public async Task DecisionAppliesIpWideWithoutServiceKey()
    {
        var path = CreatePath();
        var store = new FileRememberedRuleStore(path);
        var rule = new RememberedIpRule(
            SourceIp: "2001:db8::1",
            Decision: DriverDecisionKind.Block,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:01:00Z"));

        await store.UpsertAsync(rule, CancellationToken.None);

        var saved = await store.FindBySourceIpAsync("2001:DB8::1", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("2001:db8::1", saved.SourceIp);
        Assert.Equal(DriverDecisionKind.Block, saved.Decision);
    }

    private static string CreatePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScamAlert.Tests", Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "remembered-rules.json");
    }
}
