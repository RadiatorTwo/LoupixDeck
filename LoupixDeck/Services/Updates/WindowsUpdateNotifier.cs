#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;

namespace LoupixDeck.Services.Updates;

/// <summary>
/// Windows: a notification balloon, which Windows 10/11 show as a toast. Avalonia's tray icon does
/// not expose one, so a short-lived second notify icon on the main window's handle carries it and
/// is removed again once the toast has had time to show.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsUpdateNotifier : IUpdateNotifier
{
    private const uint NimAdd = 0x0;
    private const uint NimModify = 0x1;
    private const uint NimDelete = 0x2;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NiifUser = 0x4;
    private const uint NiifLargeIcon = 0x20;

    /// <summary>Distinct from any id Avalonia uses for the app's own tray icon.</summary>
    private const uint NotifyIconId = 0x2330;

    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    public void Show(string title, string body, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || Environment.ProcessPath is null)
        {
            return;
        }

        try
        {
            // Icon owns its HICON and destroys it on Dispose, after the notify icon is gone.
            Icon icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            NotifyIconData data = Create(windowHandle, icon?.Handle ?? IntPtr.Zero);

            data.uFlags = NifIcon | NifTip;
            SetText(data.szTip, 128, "LoupixDeck");
            if (!ShellNotifyIcon(NimAdd, ref data))
            {
                icon?.Dispose();
                Console.WriteLine("[Update] Shell_NotifyIconW(NIM_ADD) failed.");
                return;
            }

            data.uFlags = NifInfo;
            data.dwInfoFlags = NiifUser | NiifLargeIcon;
            data.hBalloonIcon = icon?.Handle ?? IntPtr.Zero;
            SetText(data.szInfoTitle, 64, title);
            SetText(data.szInfo, 256, body);
            ShellNotifyIcon(NimModify, ref data);

            _ = Task.Delay(Lifetime).ContinueWith(_ =>
            {
                NotifyIconData remove = Create(windowHandle, IntPtr.Zero);
                ShellNotifyIcon(NimDelete, ref remove);
                icon?.Dispose();
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Update] Windows notification failed: {ex.Message}");
        }
    }

    private static unsafe NotifyIconData Create(IntPtr windowHandle, IntPtr iconHandle)
    {
        return new NotifyIconData
        {
            cbSize = (uint)sizeof(NotifyIconData),
            hWnd = windowHandle,
            uID = NotifyIconId,
            hIcon = iconHandle
        };
    }

    private static unsafe void SetText(char* buffer, int capacity, string text)
    {
        ReadOnlySpan<char> value = (text ?? string.Empty).AsSpan();
        if (value.Length > capacity - 1)
        {
            value = value[..(capacity - 1)];
        }

        Span<char> target = new(buffer, capacity);
        target.Clear();
        value.CopyTo(target);
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    /// <summary>NOTIFYICONDATAW, blittable (fixed UTF-16 buffers).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
#endif
