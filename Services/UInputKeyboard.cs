using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Models;
using LoupixDeck.Utils;
// ReSharper disable CollectionNeverQueried.Local
// ReSharper disable UnusedMember.Local

namespace LoupixDeck.Services
{
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
    }

    public class UInputKeyboard : IUInputKeyboard
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

        [StructLayout(LayoutKind.Sequential)]
        private struct InputEvent
        {
            public TimeVal time;
            public ushort type;
            public ushort code;
            public int value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TimeVal
        {
            public long tv_sec;   // time_t
            public long tv_usec;  // microseconds
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UinputUserDev
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string name;
            public ushort id_bustype;
            public ushort id_vendor;
            public ushort id_product;
            public ushort id_version;
            public int ff_effects_max;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absmax;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absmin;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absfuzz;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absflat;
        }

        private struct SsizeT(IntPtr value)
        {
            public IntPtr Value = value;
        }

        private struct SizeT(int v)
        {
            public IntPtr Value = v;
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int ioctl(int fd, int request, int value);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static extern SsizeT write(int fd, IntPtr buffer, SizeT count);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int close(int fd);

        private int _fileDescriptor;
        private IntPtr _devPtr;
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
            var dev = new UinputUserDev
            {
                name = "LoupixVirtualKeyboard",
                id_bustype = 0,
                id_vendor = 0x1234,
                id_product = 0x5678,
                id_version = 1,
                absmax = new int[64],
                absmin = new int[64],
                absfuzz = new int[64],
                absflat = new int[64]
            };

            // Copy Struct to unmanaged memory
            _devPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dev));
            Marshal.StructureToPtr(dev, _devPtr, false);

            // Write user_dev-Struct to /dev/uinput
            write(_fileDescriptor, _devPtr, new SizeT(Marshal.SizeOf(dev)));

            // Create device
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
            {
                if (KeyNames.TryGetLinux(name, out var code))
                    codes.Add(code);
                else
                    Console.Error.WriteLine($"[UInputKeyboard] Unknown key name: '{name}'");
            }

            if (codes.Count == 0)
                return;

            foreach (var code in codes)
                PressKey(code);

            for (var i = codes.Count - 1; i >= 0; i--)
                ReleaseKey(codes[i]);
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Destroy device
            ioctl(_fileDescriptor, UI_DEV_DESTROY, 0);

            close(_fileDescriptor);
            _fileDescriptor = -1;

            if (_devPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_devPtr);
                _devPtr = IntPtr.Zero;
            }

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
            var inputEvent = new InputEvent
            {
                type = (ushort)type,
                code = (ushort)code,
                value = value
            };

            int size = Marshal.SizeOf(inputEvent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(inputEvent, ptr, false);

            write(_fileDescriptor, ptr, new SizeT(size));

            Marshal.FreeHGlobal(ptr);
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
    public class WindowsUInputKeyboard : IUInputKeyboard
    {
        private const int INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        private const uint MAPVK_VK_TO_VSC = 0;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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
            {
                if (KeyNames.TryGetWindows(name, out var virtualKey, out var extended))
                    keys.Add((virtualKey, extended));
                else
                    Console.Error.WriteLine($"[WindowsUInputKeyboard] Unknown key name: '{name}'");
            }

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
}
