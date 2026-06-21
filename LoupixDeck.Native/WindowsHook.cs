using LoupixDeck.Native.Types.Windows;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Native;

public sealed class WindowsHook : SafeHandleZeroOrMinusOneIsInvalid
{
    public static class HookCodes
    {
        /// <summary>
        /// The wParam and lParam parameters contain information about a keystroke message.
        /// </summary>
        public const int HC_ACTION = 0;

        /// <summary>
        /// The wParam and lParam parameters contain information about a keystroke message,
        /// and the keystroke message has not been removed from the message queue. (An application called the PeekMessage function, specifying the PM_NOREMOVE flag.)
        /// </summary>
        public const int HC_NOREMOVE = 3;
    }

    public int? HookCodeFilter { get; init; }
    private readonly LowLevelKeyboardProcHandler userHandler;
    private readonly LowLevelKeyboardProc TheHookedMethod;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (HookCodeFilter is null || nCode == HookCodeFilter)
            userHandler.Invoke(nCode, wParam, lParam);
        return User32.Hooks.CallNextHookEx(handle, nCode, wParam, lParam);
    }

    public WindowsHook(WindowsHookType type, LowLevelKeyboardProcHandler handler) : this(type, handler, User32.GetModuleHandleSelf(), 0) { }
    public WindowsHook(WindowsHookType type, LowLevelKeyboardProcHandler handler, IntPtr moduleHandle, uint threadId) : base(true)
    {
        userHandler = handler;
        TheHookedMethod = HookCallback;
        User32.Hooks.SetWindowsHookEx(type, TheHookedMethod, moduleHandle, threadId);
    }

    protected override bool ReleaseHandle()
    {
        return User32.Hooks.UnhookWindowsHookEx(handle);
    }
}
