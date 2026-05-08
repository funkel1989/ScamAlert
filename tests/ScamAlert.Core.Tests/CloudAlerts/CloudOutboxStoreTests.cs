using Microsoft.Extensions.Logging.Abstractions;
using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Core.Tests.CloudAlerts;

public sealed class CloudOutboxStoreTests
{
    [Fact]
    public async Task Enqueue_read_replace_roundtrip()
    {
        var pending = Path.Combine(Path.GetTempPath(), $"out-p-{Guid.NewGuid():N}.jsonl");
        var dead = Path.Combine(Path.GetTempPath(), $"out-d-{Guid.NewGuid():N}.jsonl");
        try
        {
            var store = new CloudOutboxStore(pending, dead, NullLogger<CloudOutboxStore>.Instance);
            var item = new CloudAlertOutboxItem
            {
                Id = Guid.NewGuid(),
                ClientEventId = Guid.NewGuid(),
                ExternalDeviceId = "d1",
                SourceIp = "203.0.113.1",
                DestinationPort = 3389,
                Service = "rdp",
                AttemptCount = 0,
                NextAttemptUtc = DateTimeOffset.UtcNow,
                EnqueuedUtc = DateTimeOffset.UtcNow
            };

            await store.EnqueueAsync(item, CancellationToken.None);
            var read = await store.ReadPendingAsync(CancellationToken.None);
            Assert.Single(read);
            Assert.Equal(item.Id, read[0].Id);

            await store.ReplacePendingAsync([], CancellationToken.None);
            read = await store.ReadPendingAsync(CancellationToken.None);
            Assert.Empty(read);
        }
        finally
        {
            try
            {
                File.Delete(pending);
            }
            catch
            {
            }

            try
            {
                File.Delete(dead);
            }
            catch
            {
            }
        }
    }
}
