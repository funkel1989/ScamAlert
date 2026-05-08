using Microsoft.Extensions.Logging.Abstractions;
using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Core.Tests.CloudAlerts;

public sealed class FileBackedAlertDeduperTests
{
    [Fact]
    public async Task Second_call_inside_window_is_suppressed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.json");
        try
        {
            var source = new FixedEnqueueSource(
                new CloudAlertEnqueueSnapshot(true, "dev1", TimeSpan.FromMinutes(10)));
            var deduper = new FileBackedAlertDeduper(path, source, NullLogger<FileBackedAlertDeduper>.Instance);

            var t0 = DateTimeOffset.Parse("2026-05-08T12:00:00Z");
            Assert.True(await deduper.TryReserveEnqueueSlotAsync("k1", t0, CancellationToken.None));
            Assert.False(await deduper.TryReserveEnqueueSlotAsync("k1", t0 + TimeSpan.FromMinutes(1), CancellationToken.None));
            Assert.True(await deduper.TryReserveEnqueueSlotAsync("k1", t0 + TimeSpan.FromMinutes(11), CancellationToken.None));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private sealed class FixedEnqueueSource(CloudAlertEnqueueSnapshot snapshot) : ICloudAlertEnqueueSource
    {
        public CloudAlertEnqueueSnapshot GetSnapshot() => snapshot;
    }
}
