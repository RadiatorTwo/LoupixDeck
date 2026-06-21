using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LoupixDeck.Native.Types.Linux;

namespace LoupixDeck.Native;

[SupportedOSPlatform("linux")]
public sealed class UInputFile : FileDescriptorBase, IDisposable
{
    public readonly struct SetupContext
    {
        private readonly UInputFile self;

        // Only for use by UInputFile.Connect
        internal SetupContext(UInputFile self) => this.self = self;

        public SetupContextKeys SetupKeys()
        {
            self.SetEvBit(Constants.EV_KEY);
            return new(self);
        }

        public SetupContextRelative SetupRelatives()
        {
            self.SetEvBit(Constants.EV_REL);
            return new(self);
        }

        internal void Deconstruct(out object keyMap, out object allKeyCodes) => throw new NotImplementedException();
    }

    public readonly struct SetupContextKeys
    {
        private readonly UInputFile self;

        // Only for use by UInputFile.Connect
        internal SetupContextKeys(UInputFile self) => this.self = self;

        public SetupContextKeys SetLeftShift()
        {
            self.SetLeftShift();
            return this;
        }

        public SetupContextKeys SetKeyBit(int keycode)
        {
            self.SetKeyBit(keycode);
            return this;
        }
    }

    public readonly struct SetupContextRelative
    {
        private readonly UInputFile self;
        // Only for use by UInputFile.Connect
        internal SetupContextRelative(UInputFile self) => this.self = self;

        public SetupContextRelative SetRelXBit() => SetRelBit(Constants.RelativeEventCodes.REL_X);
        public SetupContextRelative SetRelYBit() => SetRelBit(Constants.RelativeEventCodes.REL_Y);
        public SetupContextRelative SetRelWheelBit() => SetRelBit(Constants.RelativeEventCodes.REL_WHEEL);

        private SetupContextRelative SetRelBit(int keycode)
        {
            self.SetRelBit(keycode);
            return this;
        }
    }

    private static class Constants
    {
        public const string UINPUT_PATH = "/dev/uinput";

        public const int EV_SYN = 0x00;
        public const int EV_KEY = 0x01;
        public const int EV_REL = 0x02;

        public const int SYN_REPORT = 0;

        public const int UI_SET_EVBIT = 0x40045564;
        public const int UI_SET_KEYBIT = 0x40045565;
        public const int UI_SET_RELBIT = 0x40045566;

        public const int UI_DEV_CREATE = 0x5501;
        public const int UI_DEV_DESTROY = 0x5502;

        public static class RelativeEventCodes
        {
            public const int REL_X = 0x00;
            public const int REL_Y = 0x01;
            public const int REL_WHEEL = 0x08;
        }

        public static class KeyCodes
        {
            // Shift key
            public const int KEY_LEFTSHIFT = 42;
        }
    }

    private readonly UInputDeviceStructHandle? device;

    [MemberNotNullWhen(true, nameof(device))]
    public bool Connected { get => field && !IsInvalid && device?.IsInvalid is false; private set; }

    public static UInputFile Create(UInputDeviceStruct deviceData) => new(deviceData);
    public static UInputFile CreateKeyboard() => new(new()
            {
                Name = "LoupixVirtualKeyboard",
                id_bustype = 0,
                id_vendor = 0x1234,
                id_product = 0x5678,
                id_version = 1,
            });
    public static UInputFile CreateMouse() => new(new()
            {
                Name = "LoupixVirtualMouse",
                id_bustype = 0,
                id_vendor = 0x1234,
                id_product = 0x5679,
                id_version = 1,
            });

    public UInputFile(UInputDeviceStruct deviceData) : base(0)
    {
        // Step 1: open /dev/uinput
        try
        {
            SetHandle(OpenBare(Constants.UINPUT_PATH, FileAccess.Write, blocking: false));

            // Copy Struct to unmanaged memory
            device = new(deviceData);
        }
        catch (Exception)
        {
            // Don´t throw an Exception.
            // Just set a value, that this won´t work and get out.
            //throw new IOException("Could not open /dev/uinput. Is uinput running and are the permissions set?");
            return;
        }
    }

    public void Connect(Action<SetupContext> action, bool throwOnError = true) => Connect<Action<SetupContext>>(state: action, static (ctx, action) => action.Invoke(ctx), throwOnError);

    public void Connect<TState>(TState state, Action<SetupContext, TState> action, bool throwOnError = true)
    {
        AssertNotNull();
        try
        {
            // Step 2: Activate Events

            // Step 3: Do setup
            SetupContext ctx = new(this);
            action.Invoke(ctx, state);

            // Step 4: Create virtual device
            // Write user_dev-Struct to /dev/uinput
            this.Write(device.GetDeviceBytes());
            // Create device
            this.IoControl(Constants.UI_DEV_CREATE, 0);

            Connected = true;
        }
        catch (Exception ex)
        {
            Connected = false;
            if (throwOnError)
                throw new IOException("Could not open /dev/uinput. Is uinput running and are the permissions set?", ex);
        }
    }

    private void SetEvBit(int evbit) => IoControl(Constants.UI_SET_EVBIT, evbit);

    // SHIFT{
    private void SetLeftShift() => SetKeyBit(Constants.KeyCodes.KEY_LEFTSHIFT);

    public void SetKeyBit(int keycode)
    {
        AssertNotNull();
        IoControl(Constants.UI_SET_KEYBIT, keycode);
    }

    public void SetRelBit(int relbit)
    {
        AssertNotNull();
        IoControl(Constants.UI_SET_RELBIT, relbit);
    }

    [Conditional("DEBUG")]
    [MemberNotNull(nameof(device))]
    private void AssertNotNull()
    {
        Debug.Assert(this?.IsInvalid is false, "FileDescriptor must be initialized before use.");
        Debug.Assert(device is not null, "Device must be initialized before use.");
    }

    public void TapKey(ushort keyCode)
    {
        SendKeyEvent(keyCode, 1);
        SendKeyEvent(keyCode, 0);
    }

    public void PressKey(ushort keyCode) => SendKeyEvent(keyCode, 1); // 1 = press
    public void ReleaseKey(ushort keyCode) => SendKeyEvent(keyCode, 0); // 0 = release

    private void SendKeyEvent(ushort keyCode, int value)
    {
        SendInputEvent(Constants.EV_KEY, keyCode, value);
        SendReport();
    }

    public void SendMouseMoveRelative(int deltaX, int deltaY)
    {
        SendRelativeMouseEvent(Constants.RelativeEventCodes.REL_X, deltaX);
        SendRelativeMouseEvent(Constants.RelativeEventCodes.REL_Y, deltaY);
        SendReport();
    }
    public void SendMouseScroll(int value)
    {
        SendRelativeMouseEvent(Constants.RelativeEventCodes.REL_WHEEL, value);
        SendReport();
    }
    private void SendRelativeMouseEvent(ushort code, int value) => SendInputEvent(Constants.EV_REL, code, value);

    private void SendReport() => SendInputEvent(Constants.EV_SYN, Constants.SYN_REPORT, 0);

    private void SendInputEvent(ushort type, ushort code, int value)=> SendInputEvent(new() { type = type, code = code, value = value });

    private unsafe void SendInputEvent(in InputEvent inputEvent)
    {
        AssertNotNull();
#if DEBUG
#pragma warning disable CA1421 // This method uses runtime marshalling even when the 'DisableRuntimeMarshallingAttribute' is applied
        Debug.Assert(Marshal.SizeOf<InputEvent>() == sizeof(InputEvent));
#pragma warning restore CA1421
#endif

        IntPtr ptr = Marshal.AllocHGlobal(sizeof(InputEvent));
        try
        {
            ReadOnlySpan<InputEvent> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in inputEvent), 1);
            Span<byte> eventBytes = new(ptr.ToPointer(), sizeof(InputEvent));
            MemoryMarshal.AsBytes(inputSpan).CopyTo(eventBytes);
            bool success = this.TryWrite(eventBytes, out long bytesWritten);
            if (!success || bytesWritten != sizeof(InputEvent))
                throw new IOException($"Failed to write input event to /dev/uinput. Written bytes: {bytesWritten}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    protected override bool ReleaseHandle()
    {
        IoControl(Constants.UI_DEV_DESTROY, 0);
        device?.Close();
        return base.ReleaseHandle();
    }
}
