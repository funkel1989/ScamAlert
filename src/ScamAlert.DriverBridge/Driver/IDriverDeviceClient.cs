namespace ScamAlert.DriverBridge.Driver;

// Result of a single IOCTL_SCAMALERT_GET_EVENT call.
public enum DriverEventPollOutcome
{
    EventReady,
    NoEvents,
    DeviceUnavailable
}

public sealed record DriverEventPollResult(
    DriverEventPollOutcome Outcome,
    DriverEvent? Event,
    int? Win32Error);

public interface IDriverDeviceClient : IDisposable
{
    bool IsOpen { get; }

    void Open();

    void Close();

    DriverEventPollResult PollNextEvent();

    void CompleteEvent(NativeConnectionDecision decision);
}
