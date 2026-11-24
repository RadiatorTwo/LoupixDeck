using System.Runtime.InteropServices;

namespace LoupixDeck.Services.TextPaste;

/// <summary>
/// Win32 clipboard for <see cref="TextPaster"/>. Works on the raw API instead of Avalonia's
/// clipboard so it can (a) snapshot every memory-based format, not just text, and (b) keep the
/// pasted text and the restored content out of the clipboard history (Win+V) and cloud sync.
/// </summary>
internal sealed partial class WindowsPasteClipboard : IPasteClipboard
{
    private const uint CF_UNICODETEXT = 13;

    // Formats whose handle is a GDI object or owner-drawn, not global memory; they cannot be
    // copied byte-wise. Windows synthesizes CF_BITMAP again from a restored CF_DIB.
    private static readonly HashSet<uint> HandleFormats =
    [
        2,    // CF_BITMAP
        3,    // CF_METAFILEPICT
        9,    // CF_PALETTE
        14,   // CF_ENHMETAFILE
        0x80, // CF_OWNERDISPLAY
        0x82, // CF_DSPBITMAP
        0x83, // CF_DSPMETAFILEPICT
        0x8E  // CF_DSPENHMETAFILE
    ];

    // CF_PRIVATEFIRST..CF_GDIOBJLAST: private handles that mean nothing outside their owner.
    private const uint PrivateFirst = 0x0200;
    private const uint PrivateLast = 0x03FF;

    private const uint GMEM_MOVEABLE = 0x0002;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private const int OpenAttempts = 20;
    private const int OpenRetryMs = 10;

    private uint _sequenceAfterSet;

    public Task<object> CaptureAsync() => Task.Run<object>(Capture);

    public Task<bool> SetTextAsync(string text) => Task.Run(() => SetText(text));

    public Task<bool> StillHoldsPastedTextAsync() =>
        Task.FromResult(GetClipboardSequenceNumber() == _sequenceAfterSet);

    public Task RestoreAsync(object snapshot) =>
        Task.Run(() => Restore((List<(uint Format, byte[] Data)>)snapshot));

    private static List<(uint Format, byte[] Data)> Capture()
    {
        List<(uint, byte[])> formats = [];
        if (!WithClipboard(IntPtr.Zero, () =>
            {
                uint format = 0;
                while ((format = EnumClipboardFormats(format)) != 0)
                {
                    if (HandleFormats.Contains(format) || (format is >= PrivateFirst and <= PrivateLast))
                        continue;

                    byte[] data = ReadGlobal(GetClipboardData(format));
                    if (data != null)
                        formats.Add((format, data));
                }
            }))
        {
            return null;
        }

        return formats;
    }

    private bool SetText(string text)
    {
        byte[] bytes = new byte[(text.Length + 1) * sizeof(char)];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);

        bool ok = WithOwnedClipboard(() =>
        {
            if (!SetGlobal(CF_UNICODETEXT, bytes))
                return false;
            MarkPrivate();
            return true;
        });

        _sequenceAfterSet = GetClipboardSequenceNumber();
        return ok;
    }

    private static void Restore(List<(uint Format, byte[] Data)> snapshot)
    {
        bool ok = WithOwnedClipboard(() =>
        {
            foreach ((uint format, byte[] data) in snapshot)
                SetGlobal(format, data);

            // The content was in the history already; restoring it must not add it twice.
            if (snapshot.Count > 0)
                MarkPrivate();
            return true;
        });

        if (!ok)
            Console.Error.WriteLine("[TextPaster] Could not restore the previous clipboard content.");
    }

    /// <summary>
    /// Opens the clipboard with a short-lived message-only window as owner, empties it and runs
    /// <paramref name="write"/>. EmptyClipboard with a null owner makes SetClipboardData fail
    /// according to the documentation, hence the window.
    /// </summary>
    private static bool WithOwnedClipboard(Func<bool> write)
    {
        IntPtr hwnd = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, HWND_MESSAGE,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            bool result = false;
            bool opened = WithClipboard(hwnd, () =>
            {
                if (EmptyClipboard())
                    result = write();
            });
            return opened && result;
        }
        finally
        {
            // The data stays on the clipboard; only delayed-rendered formats would need the
            // owner, and none are used here.
            DestroyWindow(hwnd);
        }
    }

    // Another application may hold the clipboard open for a moment; retry briefly.
    private static bool WithClipboard(IntPtr owner, Action action)
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(owner))
            {
                try
                {
                    action();
                }
                finally
                {
                    CloseClipboard();
                }
                return true;
            }

            Thread.Sleep(OpenRetryMs);
        }

        Console.Error.WriteLine("[TextPaster] The clipboard is held open by another application.");
        return false;
    }

    // Keeps the current content out of the clipboard history, cloud sync and clipboard monitors.
    private static void MarkPrivate()
    {
        byte[] zero = new byte[sizeof(uint)];
        SetGlobal(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), zero);
        SetGlobal(RegisterClipboardFormat("CanIncludeInClipboardHistory"), zero);
        SetGlobal(RegisterClipboardFormat("CanUploadToCloudClipboard"), zero);
    }

    private static byte[] ReadGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            byte[] data = new byte[(int)GlobalSize(handle)];
            Marshal.Copy(pointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static bool SetGlobal(uint format, byte[] data)
    {
        if (format == 0)
            return false;

        IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(data.Length, 1));
        if (handle == IntPtr.Zero)
            return false;

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        Marshal.Copy(data, 0, pointer, data.Length);
        GlobalUnlock(handle);

        // On success the system owns the memory; on failure it is still ours to free.
        if (SetClipboardData(format, handle) != IntPtr.Zero)
            return true;

        GlobalFree(handle);
        return false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string lpszFormat);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial UIntPtr GlobalSize(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr hMem);
}
