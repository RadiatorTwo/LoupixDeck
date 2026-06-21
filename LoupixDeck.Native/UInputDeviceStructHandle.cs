using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Linux;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Native;

public sealed class UInputDeviceStructHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public UInputDeviceStructHandle() : this(UInputDeviceStruct.Default()) { }
    public unsafe UInputDeviceStructHandle(UInputDeviceStruct device) : base(true)
    {
        SetHandle(Marshal.AllocHGlobal(sizeof(UInputDeviceStruct)));
        Unsafe.AsRef<UInputDeviceStruct>((void*) handle) = device;
    }

    public unsafe ReadOnlySpan<byte> GetDeviceBytes() => new((void*)handle, sizeof(UInputDeviceStruct));

    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        handle = IntPtr.Zero;
        return true;
    }
}
