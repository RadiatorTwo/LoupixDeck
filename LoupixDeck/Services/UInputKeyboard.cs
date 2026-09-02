using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LoupixDeck.Models;
using LoupixDeck.Utils;
// ReSharper disable CollectionNeverQueried.Local
// ReSharper disable UnusedMember.Local

namespace LoupixDeck.Services;

public interface IUInputKeyboard : IDisposable
{
    public bool Connected { get; set; }

    /// <summary>
    /// Sends a single keycode as a key press and release.
    /// </summary>
    /// <param name="keyCode">Linux key code (e.g. 30 = KEY_A).</param>
    void SendKey(int keyCode);

    /// <summary>
    /// Sends a complete text, letter by letter.
    /// Currently only supports single a-z, A-Z and spaces.
    /// </summary>
    /// <param name="text">Text to be sent</param>
    void SendText(string text);

    /// <summary>
    /// Sends a key combination (e.g. ["Ctrl","C"]): all keys are pressed in order and
    /// released in reverse order. Key names are resolved via <see cref="Utils.KeyNames"/>.
    /// </summary>
    /// <param name="keyNames">Ordered list of key names making up the combination.</param>
    void SendKeyCombination(IReadOnlyList<string> keyNames);

    /// <summary>
    /// Presses a key and keeps it held down until <see cref="KeyUp"/> is called for the
    /// same key. Key names are resolved via <see cref="Utils.KeyNames"/>.
    /// </summary>
    void KeyDown(string keyName);

    /// <summary>
    /// Releases a key previously held down by <see cref="KeyDown"/>.
    /// </summary>
    void KeyUp(string keyName);
}

public partial class UInputKeyboard : IUInputKeyboard
{
    private readonly KeyboardLayout _layout;
    private const string UINPUT_PATH = "/dev/uinput";

    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;

    private const int UI_SET_EVBIT = 0x40045564;
    private const int UI_SET_KEYBIT = 0x40045565;

    private const int EV_SYN = 0x00;
    private const int EV_KEY = 0x01;

    private const int UI_DEV_CREATE = 0x5501;
    private const int UI_DEV_DESTROY = 0x5502;

    private const int SYN_REPORT = 0;

    // Shift key
    private const int KEY_LEFTSHIFT = 42;

    // linux-x64 input_event: timeval (16) + type/code (4) + value (4).
    private const int InputEventSize = 24;

    // uinput_user_dev: name[80] + input_id (8) + ff_effects_max (4) + 4 * abs[64].
    private const int UinputMaxNameSize = 80;
    private const int AbsCnt = 64;
    private const int UinputUserDevSize = UinputMaxNameSize + 8 + 4 + (AbsCnt * 4 * 4);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int ioctl(int fd, int request, int value);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint write(int fd, ReadOnlySpan<byte> buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int close(int fd);

    private int _fileDescriptor;
    private bool _disposed;

    public bool Connected { get; set; }

    public UInputKeyboard()
    {
        var localLayout = GetCurrentKeyboardLayout();
        _layout = KeyboardLayouts.GetLayout(localLayout);
        
        // Step 1: open /dev/uinput
        try
        {
            _fileDescriptor = open(UINPUT_PATH, O_WRONLY | O_NONBLOCK);
        }
        catch (Exception)
        {
            Connected = false;
            return;
        }

        if (_fileDescriptor < 0)
        {
            // Don´t throw an Exception.
            // Just set a value, that this won´t work and get out.
            //throw new IOException("Could not open /dev/uinput. Is uinput running and are the permissions set?");
            Connected = false;
            return;
        }

        // Step 2: Activate Events
        ioctl(_fileDescriptor, UI_SET_EVBIT, EV_KEY);

        // Set keybits for the letters + SHIFT
        foreach (var keyCode in _layout.KeyMap)
        {
            ioctl(_fileDescriptor, UI_SET_KEYBIT, keyCode.Value.keycode);
        }

        // SHIFT
        ioctl(_fileDescriptor, UI_SET_KEYBIT, KEY_LEFTSHIFT);

        // Keys usable in key combinations (modifiers, function keys, navigation, ...).
        // uinput only emits events for keys whose keybit was registered before UI_DEV_CREATE.
        foreach (var keyCode in KeyNames.AllLinuxKeyCodes)
        {
            ioctl(_fileDescriptor, UI_SET_KEYBIT, keyCode);
        }

        // Step 3: Create virtual device
        WriteUserDev("LoupixVirtualKeyboard", vendor: 0x1234, product: 0x5678);
        ioctl(_fileDescriptor, UI_DEV_CREATE, 0);

        Connected = true;
    }

    /// <summary>
    /// Sends a single keycode (press + release).
    /// </summary>
    public void SendKey(int keyCode)
    {
        if (!Connected)
        {
            return;
        }

        PressKey(keyCode);
        ReleaseKey(keyCode);
    }

    /// <summary>
    /// Sends a complete text (simplified, only a-z, A-Z, spaces).
    /// </summary>
    public void SendText(string text)
    {
        if (!Connected)
            return;

        foreach (var c in text)
        {
            if (!_layout.KeyMap.TryGetValue(c, out var keyCode))
            {
                // Optional: log or skip unsupported characters
                continue;
            }

            if (keyCode.shift)
                PressKey(KEY_LEFTSHIFT);

            PressKey(keyCode.keycode);
            ReleaseKey(keyCode.keycode);
            
            if (keyCode.shift)
                ReleaseKey(KEY_LEFTSHIFT);

            Thread.Sleep(1); // Small delay between keystrokes
        }
    }

    /// <summary>
    /// Presses every key of the combination in order, then releases them in reverse order.
    /// </summary>
    public void SendKeyCombination(IReadOnlyList<string> keyNames)
    {
        if (!Connected || keyNames == null || keyNames.Count == 0)
            return;

        var codes = new List<int>(keyNames.Count);
        foreach (var name in keyNames)
            codes.AddRange(Resolve(name));

        if (codes.Count == 0)
            return;

        foreach (var code in codes)
            PressKey(code);

        for (var i = codes.Count - 1; i >= 0; i--)
            ReleaseKey(codes[i]);
    }

    public void KeyDown(string keyName)
    {
        if (!Connected)
            return;

        foreach (var code in Resolve(keyName))
            PressKey(code);
    }

    public void KeyUp(string keyName)
    {
        if (!Connected)
            return;

        // Reverse order, so a shifted character releases its key before the Shift.
        var codes = Resolve(keyName);
        for (var i = codes.Count - 1; i >= 0; i--)
            ReleaseKey(codes[i]);
    }

    /// <summary>
    /// Resolves a key name to the evdev codes that produce it, in press order: an optional
    /// Shift prefix followed by the key itself. Single characters ("ü", "#", "z") are looked
    /// up in the active layout first — their physical position is layout-specific, so only
    /// the layout can answer it — everything else comes from the position table in
    /// <see cref="KeyNames"/>. An unknown name resolves to nothing and is logged.
    /// </summary>
    private List<int> Resolve(string name)
    {
        if (KeyNames.TryGetCharacter(name, out var character) &&
            _layout.KeyMap.TryGetValue(character, out var mapped))
        {
            return mapped.shift ? [KEY_LEFTSHIFT, mapped.keycode] : [mapped.keycode];
        }

        if (KeyNames.TryGetLinux(name, out var code))
            return [code];

        Console.Error.WriteLine($"[UInputKeyboard] Unknown key name: '{name}'");
        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Destroy device
        ioctl(_fileDescriptor, UI_DEV_DESTROY, 0);

        close(_fileDescriptor);
        _fileDescriptor = -1;

        _disposed = true;
    }

    private void PressKey(int keyCode)
    {
        SendKeyEvent(keyCode, 1); // 1 = press
    }

    private void ReleaseKey(int keyCode)
    {
        SendKeyEvent(keyCode, 0); // 0 = release
    }

    private void SendKeyEvent(int keyCode, int value)
    {
        SendInputEvent(EV_KEY, keyCode, value);
        // EV_SYN: Send “Syn-Report”
        SendInputEvent(EV_SYN, SYN_REPORT, 0);
    }

    private void SendInputEvent(int type, int code, int value)
    {
        Span<byte> buffer = stackalloc byte[InputEventSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(16), (ushort)type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(18), (ushort)code);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20), value);
        write(_fileDescriptor, buffer, (nuint)InputEventSize);
    }

    private void WriteUserDev(string name, ushort vendor, ushort product)
    {
        Span<byte> buffer = stackalloc byte[UinputUserDevSize];
        buffer.Clear();
        Encoding.ASCII.GetBytes(name, buffer.Slice(0, UinputMaxNameSize - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(82), vendor);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(84), product);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(86), 1);
        write(_fileDescriptor, buffer, (nuint)UinputUserDevSize);
    }
    
    private string GetCurrentKeyboardLayout()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "localectl",
                    Arguments = "status",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Layout:"))
                {
                    return line.Split(':')[1].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[KeyboardLayout] Error with localectl: {ex.Message}");
        }

        // Fallback:
        return "us";
    }
}

/// <summary>
/// Windows implementation backed by the Win32 <c>SendInput</c> API (user32.dll).
/// No kernel driver, no admin rights and no third-party dependency: input is injected
/// into the session input stream and delivered to the focused window, like a normal
/// keyboard. Text is sent layout-independently via Unicode injection; key combinations
/// use virtual-key codes.
///
/// Note: injected events carry the LLKHF_INJECTED flag, so apps reading raw input
/// (some games / anti-cheat) may ignore them — that is a fundamental limit of any
/// user-mode injection and cannot be bypassed without a kernel driver.
/// </summary>
public partial class WindowsUInputKeyboard : IUInputKeyboard
{
    private const int INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const uint MAPVK_VK_TO_VSC = 0;

    // Scan code -> virtual key, distinguishing left/right modifiers and honouring the E0
    // prefix. Used to resolve punctuation/OEM positions, whose VK differs per layout.
    private const uint MAPVK_VSC_TO_VK_EX = 3;

    // Virtual keys for the modifiers a shifted character may need.
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_RMENU = 0xA5; // AltGr — Windows reports it as Ctrl+Alt

    // VkKeyScanEx shift-state bits (high byte of the return value).
    private const int ShiftStateShift = 1;
    private const int ShiftStateCtrl = 2;
    private const int ShiftStateAlt = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Only the keyboard variant is used, but the union must be sized to the largest
    // member (MOUSEINPUT) so Marshal.SizeOf<INPUT>() matches the size SendInput expects.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, ReadOnlySpan<INPUT> pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint uCode, uint uMapType);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [LibraryImport("user32.dll", EntryPoint = "VkKeyScanExW")]
    private static partial short VkKeyScanEx(ushort ch, IntPtr dwhkl);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // SendInput requires no setup, so the backend is always available on Windows.
    public bool Connected { get; set; } = true;

    public void SendKey(int keyCode)
    {
        if (!Connected)
            return;

        // keyCode is treated as a virtual-key code (interface compatibility).
        var inputs = new[]
        {
            KeyInput(keyCode, false, false),
            KeyInput(keyCode, false, true)
        };
        Send(inputs);
    }

    public void SendText(string text)
    {
        if (!Connected || string.IsNullOrEmpty(text))
            return;

        // Unicode injection: send each UTF-16 code unit directly, independent of the
        // active keyboard layout (handles umlauts, accents, emoji, ...).
        var inputs = new INPUT[text.Length * 2];
        var i = 0;
        foreach (var c in text)
        {
            inputs[i++] = UnicodeInput(c, false);
            inputs[i++] = UnicodeInput(c, true);
        }

        Send(inputs);
    }

    public void SendKeyCombination(IReadOnlyList<string> keyNames)
    {
        if (!Connected || keyNames == null || keyNames.Count == 0)
            return;

        var keys = new List<(int virtualKey, bool extended)>(keyNames.Count);
        foreach (var name in keyNames)
            keys.AddRange(Resolve(name));

        if (keys.Count == 0)
            return;

        // Press all keys in order, then release them in reverse order.
        var inputs = new INPUT[keys.Count * 2];
        var i = 0;
        foreach (var (virtualKey, extended) in keys)
            inputs[i++] = KeyInput(virtualKey, extended, false);

        for (var k = keys.Count - 1; k >= 0; k--)
            inputs[i++] = KeyInput(keys[k].virtualKey, keys[k].extended, true);

        Send(inputs);
    }

    public void KeyDown(string keyName)
    {
        SendSingle(keyName, up: false);
    }

    public void KeyUp(string keyName)
    {
        SendSingle(keyName, up: true);
    }

    private void SendSingle(string keyName, bool up)
    {
        if (!Connected)
            return;

        var keys = Resolve(keyName);
        if (keys.Count == 0)
            return;

        // Press in order, release in reverse, so a shifted character releases its key
        // before the Shift it needed.
        var inputs = new INPUT[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            var (virtualKey, extended) = up ? keys[keys.Count - 1 - i] : keys[i];
            inputs[i] = KeyInput(virtualKey, extended, up);
        }

        Send(inputs);
    }

    /// <summary>
    /// Resolves a key name to the virtual keys that produce it, in press order: the modifiers
    /// a character needs (Shift / AltGr) followed by the key itself.
    ///
    /// Three sources are tried in turn: the layout-stable table in <see cref="KeyNames"/>,
    /// then single characters ("ü", "#", "+") via <c>VkKeyScanEx</c> against the active
    /// layout, then the punctuation/OEM positions via their scan code — those carry a
    /// different VK on every layout, so only <c>MapVirtualKeyEx</c> can answer for the live
    /// one. An unknown name resolves to nothing and is logged.
    /// </summary>
    private static List<(int virtualKey, bool extended)> Resolve(string name)
    {
        if (KeyNames.TryGetWindows(name, out var tableKey, out var tableExtended))
            return [(tableKey, tableExtended)];

        var layout = ActiveLayout();

        if (KeyNames.TryGetCharacter(name, out var character))
        {
            var scan = VkKeyScanEx(character, layout);
            if (scan != -1)
            {
                var keys = new List<(int, bool)>(2);
                var shiftState = (scan >> 8) & 0xFF;

                // Ctrl+Alt is how Windows spells AltGr; send the physical AltGr key for it.
                if ((shiftState & ShiftStateCtrl) != 0 && (shiftState & ShiftStateAlt) != 0)
                {
                    keys.Add((VK_RMENU, true));
                }
                else
                {
                    if ((shiftState & ShiftStateCtrl) != 0) keys.Add((VK_CONTROL, false));
                    if ((shiftState & ShiftStateAlt) != 0) keys.Add((VK_MENU, false));
                }

                if ((shiftState & ShiftStateShift) != 0)
                    keys.Add((VK_SHIFT, false));

                keys.Add((scan & 0xFF, false));
                return keys;
            }
        }

        if (KeyNames.TryGetInterception(name, out var scanCode, out var e0))
        {
            var virtualKey = MapVirtualKeyEx(e0 ? 0xE000u | (uint)scanCode : (uint)scanCode,
                MAPVK_VSC_TO_VK_EX, layout);
            if (virtualKey != 0)
                return [((int)virtualKey, e0)];
        }

        Console.Error.WriteLine($"[WindowsUInputKeyboard] Unknown key name: '{name}'");
        return [];
    }

    // Keyboard layout of the window that will receive the input. Layouts are per-thread on
    // Windows, so the foreground window's layout — not this app's — decides where a character
    // sits. Falls back to the calling thread's layout when there is no foreground window.
    private static IntPtr ActiveLayout()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return GetKeyboardLayout(0);

        var thread = GetWindowThreadProcessId(window, IntPtr.Zero);
        return GetKeyboardLayout(thread);
    }

    public void Dispose()
    {
        // Nothing to dispose — SendInput holds no resources.
    }

    private static INPUT KeyInput(int virtualKey, bool extended, bool up)
    {
        var flags = 0u;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC),
                    dwFlags = flags
                }
            }
        };
    }

    private static INPUT UnicodeInput(char c, bool up)
    {
        var flags = KEYEVENTF_UNICODE;
        if (up) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = flags
                }
            }
        };
    }

    private static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;

        SendInput((uint)inputs.Length, inputs, InputSize);
    }
}
