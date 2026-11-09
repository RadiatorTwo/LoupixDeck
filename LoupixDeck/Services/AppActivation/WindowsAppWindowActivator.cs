#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Windows implementation: finds the top-level windows owned by processes of the given name and
/// hands the foreground to the most recently used one.
/// </summary>
/// <remarks>
/// <para>
/// Windows only lets the process that received the last input event move the foreground, and a
/// deck button press is not input Windows knows about. <c>AttachThreadInput</c> to the current
/// foreground thread shares its input state and lifts that restriction in the common case; when
/// it does not, a synthesized Alt press — the documented condition "the foreground lock is
/// released while a key is down" — is the fallback.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsAppWindowActivator : IAppWindowActivator
{
    private const int GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const int SW_RESTORE = 9;
    private const int DWMWA_CLOAKED = 14;
    private const int DWM_CLOAKED_APP = 0x1;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;

    public Task<AppActivationOutcome> ActivateAsync(string processName, IReadOnlyCollection<string> windowClasses,
        CancellationToken cancellationToken)
        => Task.Run(() => Activate(processName), cancellationToken);

    private static AppActivationOutcome Activate(string processName)
    {
        HashSet<uint> processIds = ProcessIds(processName);
        if (processIds.Count == 0)
            return AppActivationOutcome.NotRunning;

        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && processIds.Contains(ProcessIdOf(foreground)))
            return AppActivationOutcome.AlreadyFocused;

        IntPtr window = FindWindow(processIds);
        if (window == IntPtr.Zero)
            return AppActivationOutcome.NoWindow;

        if (!Focus(window))
        {
            // Windows refused the foreground switch and flashes the taskbar button instead. A
            // second instance would not be better, so this still counts as handled.
            Console.WriteLine($"System.LaunchApp: Windows refused to focus '{processName}'.");
        }

        return AppActivationOutcome.Activated;
    }

    private static HashSet<uint> ProcessIds(string processName)
    {
        HashSet<uint> ids = [];
        foreach (Process process in Process.GetProcessesByName(ContextRuleMatcher.Normalize(processName)))
        {
            using (process)
                ids.Add((uint)process.Id);
        }

        return ids;
    }

    private static uint ProcessIdOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }

    /// <summary>
    /// The first window, in Z order, that a user would recognise as the application: visible,
    /// unowned, not a tool window, and not cloaked by the application itself. Z order puts the
    /// most recently used window first. A window cloaked by the shell sits on another virtual
    /// desktop and still counts — focusing it switches to that desktop.
    /// </summary>
    private static unsafe IntPtr FindWindow(HashSet<uint> processIds)
    {
        WindowSearch search = new(processIds);
        GCHandle handle = GCHandle.Alloc(search);
        try
        {
            EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return search.Found;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(IntPtr window, IntPtr state)
    {
        // Native callback: an exception must never unwind into user32.
        try
        {
            WindowSearch search = (WindowSearch)GCHandle.FromIntPtr(state).Target;
            if (!search.ProcessIds.Contains(ProcessIdOf(window)) || !IsApplicationWindow(window))
                return 1;

            search.Found = window;
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static bool IsApplicationWindow(IntPtr window)
    {
        if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER) != IntPtr.Zero)
            return false;

        if ((GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0)
            return false;

        return DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0
               || (cloaked & DWM_CLOAKED_APP) == 0;
    }

    private static bool Focus(IntPtr window)
    {
        if (IsIconic(window))
            ShowWindow(window, SW_RESTORE);

        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint ownThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != ownThread
                        && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
                AttachThreadInput(ownThread, foregroundThread, false);
        }

        if (GetForegroundWindow() == window)
            return true;

        // Holding Alt releases the foreground lock. Released only after the switch, so the key-up
        // reaches the activated window as an unpaired release, which opens no menu.
        SendAlt(keyUp: false);
        SetForegroundWindow(window);
        SendAlt(keyUp: true);
        return GetForegroundWindow() == window;
    }

    private static unsafe void SendAlt(bool keyUp)
    {
        INPUT input = new()
        {
            Type = INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { VirtualKey = VK_MENU, Flags = keyUp ? KEYEVENTF_KEYUP : 0 }
        };
        SendInput(1, &input, sizeof(INPUT));
    }

    private sealed class WindowSearch(HashSet<uint> processIds)
    {
        public HashSet<uint> ProcessIds { get; } = processIds;
        public IntPtr Found { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>
    /// INPUT with its union sized for the largest member, MOUSEINPUT, so SendInput accepts
    /// <c>sizeof(INPUT)</c> (40 bytes on x64, 28 on x86).
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KEYBDINPUT Keyboard;
        [FieldOffset(8)] private MouseInputPadding _mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPadding
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr state);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr window, int command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr window, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    private static unsafe partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
#endif
