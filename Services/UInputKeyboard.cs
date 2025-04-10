using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
    }

    public class UInputKeyboard : IUInputKeyboard
    {
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

        // Shift for capital letters
        private const int KEY_LEFTSHIFT = 42;

        // Example: Here are some of the keys (a-z, SPACE, SHIFT).
        // The codes come from linux/input-event-codes.h
        private static readonly Dictionary<char, int> KeyCodeMap = new Dictionary<char, int>
        {
            // Lower case
            ['a'] = 30,
            ['b'] = 48,
            ['c'] = 46,
            ['d'] = 32,
            ['e'] = 18,
            ['f'] = 33,
            ['g'] = 34,
            ['h'] = 35,
            ['i'] = 23,
            ['j'] = 36,
            ['k'] = 37,
            ['l'] = 38,
            ['m'] = 50,
            ['n'] = 49,
            ['o'] = 24,
            ['p'] = 25,
            ['q'] = 16,
            ['r'] = 19,
            ['s'] = 31,
            ['t'] = 20,
            ['u'] = 22,
            ['v'] = 47,
            ['w'] = 17,
            ['x'] = 45,
            ['y'] = 21,
            ['z'] = 44,
            
            ['0'] = 11,
            ['1'] = 2,
            ['2'] = 3,
            ['3'] = 4,
            ['4'] = 5,
            ['5'] = 6,
            ['6'] = 7,
            ['7'] = 8,
            ['8'] = 9,
            ['9'] = 10,

            // Space
            [' '] = 57
        };


        [StructLayout(LayoutKind.Sequential)]
        private struct input_event
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
        private struct uinput_user_dev
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

        private struct ssize_t
        {
            public IntPtr Value;

            public ssize_t(IntPtr value)
            {
                Value = value;
            }
        }

        private struct size_t
        {
            public IntPtr Value;
            public size_t(int v) { Value = (IntPtr)v; }
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int ioctl(int fd, int request, int value);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static extern ssize_t write(int fd, IntPtr buffer, size_t count);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int close(int fd);

        private int _fileDescriptor;
        private IntPtr _devPtr;
        private bool _disposed;

        public bool Connected { get; set; }

        public UInputKeyboard()
        {
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
            foreach (var keyCode in KeyCodeMap.Values)
            {
                ioctl(_fileDescriptor, UI_SET_KEYBIT, keyCode);
            }

            // SHIFT
            ioctl(_fileDescriptor, UI_SET_KEYBIT, KEY_LEFTSHIFT);

            // Step 3: Create virtual device
            var dev = new uinput_user_dev
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
            write(_fileDescriptor, _devPtr, new size_t(Marshal.SizeOf(dev)));

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
            {
                return;
            }

            foreach (char c in text)
            {
                // Check if capital letter
                bool isUpperCase = char.IsUpper(c);

                // Convert character to lowercase if uppercase
                char lowerChar = char.ToLower(c);

                // Is there a KeyCode for this?
                if (!KeyCodeMap.TryGetValue(lowerChar, out int keyCode))
                {
                    // Optional: ignore or treat other characters
                    continue;
                }

                // Press SHIFT for capital letter
                if (isUpperCase)
                    PressKey(KEY_LEFTSHIFT);

                // Press + release button (e.g. 'a')
                PressKey(keyCode);
                ReleaseKey(keyCode);

                // Release SHIFT again
                if (isUpperCase)
                    ReleaseKey(KEY_LEFTSHIFT);

                // Small waiting time between buttons if necessary
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
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
            var inputEvent = new input_event
            {
                type = (ushort)type,
                code = (ushort)code,
                value = value
            };

            int size = Marshal.SizeOf(inputEvent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(inputEvent, ptr, false);

            write(_fileDescriptor, ptr, new size_t(size));

            Marshal.FreeHGlobal(ptr);
        }
    }
}
