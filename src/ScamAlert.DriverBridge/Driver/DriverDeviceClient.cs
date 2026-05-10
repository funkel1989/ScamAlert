using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScamAlert.DriverBridge.Driver;

// Real implementation of IDriverDeviceClient. Wraps CreateFile +
// DeviceIoControl against \\.\ScamAlertWfp.
public sealed class DriverDeviceClient(string devicePath) : IDriverDeviceClient
{
    private SafeFileHandle? _handle;

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    public void Open()
    {
        if (IsOpen) return;
        _handle = NativeMethods.OpenDevice(devicePath);
    }

    public void Close()
    {
        _handle?.Dispose();
        _handle = null;
    }

    public DriverEventPollResult PollNextEvent()
    {
        if (!IsOpen)
        {
            return new DriverEventPollResult(DriverEventPollOutcome.DeviceUnavailable, null, null);
        }

        var size = Marshal.SizeOf<NativeConnectionEvent>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            // Zero out so partially-populated reads do not leak prior values.
            for (var offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(buffer, offset, 0);
            }

            var (ok, err, bytesReturned) = NativeMethods.DeviceIoControlOut(
                _handle!,
                NativeDriverIoctl.IoctlGetEvent,
                buffer,
                (uint)size);

            if (!ok)
            {
                if (err == NativeMethods.ErrorNoMoreItems)
                {
                    return new DriverEventPollResult(DriverEventPollOutcome.NoEvents, null, err);
                }

                Close();
                return new DriverEventPollResult(DriverEventPollOutcome.DeviceUnavailable, null, err);
            }

            if (bytesReturned < size)
            {
                throw new InvalidOperationException(
                    $"IOCTL_SCAMALERT_GET_EVENT returned {bytesReturned} bytes, expected {size}.");
            }

            var native = Marshal.PtrToStructure<NativeConnectionEvent>(buffer);
            var driverEvent = DriverEventMarshaller.ToDriverEvent(native);
            return new DriverEventPollResult(DriverEventPollOutcome.EventReady, driverEvent, null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void CompleteEvent(NativeConnectionDecision decision)
    {
        if (!IsOpen) return;

        var size = Marshal.SizeOf<NativeConnectionDecision>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(decision, buffer, fDeleteOld: false);
            var (ok, err, _) = NativeMethods.DeviceIoControlIn(
                _handle!,
                NativeDriverIoctl.IoctlCompleteEvent,
                buffer,
                (uint)size);

            if (!ok)
            {
                throw new InvalidOperationException(
                    $"IOCTL_SCAMALERT_COMPLETE_EVENT failed (Win32 error {err}).");
            }
        }
        finally
        {
            Marshal.DestroyStructure<NativeConnectionDecision>(buffer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => Close();
}
