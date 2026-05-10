using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScamAlert.DriverBridge.Driver;

internal static class NativeMethods
{
    public const uint GenericRead       = 0x80000000;
    public const uint GenericWrite      = 0x40000000;
    public const uint FileShareRead     = 0x00000001;
    public const uint FileShareWrite    = 0x00000002;
    public const uint OpenExisting      = 3;
    public const uint FileAttributeNormal = 0x80;

    public const int ErrorNoMoreItems = 259; // STATUS_NO_MORE_ENTRIES translated by ntdll.

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        nint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    public static SafeFileHandle OpenDevice(string devicePath)
    {
        var handle = CreateFileW(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            0,
            OpenExisting,
            FileAttributeNormal,
            0);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"CreateFile('{devicePath}') failed (Win32 error {error}).");
        }

        return handle;
    }

    public static (bool Success, int Win32Error, uint BytesReturned) DeviceIoControlIn(
        SafeFileHandle handle,
        uint controlCode,
        nint inBuffer,
        uint inBufferSize)
    {
        var ok = DeviceIoControl(handle, controlCode, inBuffer, inBufferSize, 0, 0, out var bytesReturned, 0);
        var err = ok ? 0 : Marshal.GetLastWin32Error();
        return (ok, err, bytesReturned);
    }

    public static (bool Success, int Win32Error, uint BytesReturned) DeviceIoControlOut(
        SafeFileHandle handle,
        uint controlCode,
        nint outBuffer,
        uint outBufferSize)
    {
        var ok = DeviceIoControl(handle, controlCode, 0, 0, outBuffer, outBufferSize, out var bytesReturned, 0);
        var err = ok ? 0 : Marshal.GetLastWin32Error();
        return (ok, err, bytesReturned);
    }
}
