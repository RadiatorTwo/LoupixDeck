using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Native;

public static partial class User32
{
    public static partial class Hooks
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial IntPtr SetWindowsHookEx(WindowsHookType idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
            uint dwThreadId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWindowsHookEx(IntPtr hhk);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }
}
