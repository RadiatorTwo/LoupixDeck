#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Reads the target out of a Windows <c>.lnk</c> shortcut through <c>IShellLinkW</c>.
/// </summary>
/// <remarks>
/// One instance is created and reused for a whole scan. The obvious alternative — late-bound
/// <c>WScript.Shell</c> through <c>dynamic</c> — activates a fresh COM server for every shortcut
/// and pulls in the C# runtime binder, which is not trimming-safe.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutResolver : IDisposable
{
    private const int MaxPath = 260;
    private const uint SlgpRawPath = 0x0004;

    private IShellLinkW _link;
    private bool _disposed;
    private bool _unavailable;

    /// <summary>
    /// Returns the executable a shortcut points at, or null when it does not resolve to an existing
    /// file. Not thread-safe: the underlying COM object is single-use state, so one resolver belongs
    /// to one scan.
    /// </summary>
    public string Resolve(string shortcutPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_unavailable || string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        try
        {
            _link ??= (IShellLinkW)new ShellLink();

            ((IPersistFile)_link).Load(shortcutPath, 0);

            StringBuilder target = new(MaxPath);
            // SLGP_RAWPATH keeps environment variables unexpanded rather than guessing; resolving
            // is deliberately skipped, since Resolve() can trigger an installer or a network probe.
            _link.GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);

            string path = Environment.ExpandEnvironmentVariables(target.ToString());
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch (COMException ex)
        {
            Console.WriteLine($"[AppDiscovery] Cannot resolve shortcut '{shortcutPath}': {ex.Message}");

            // The COM object may be left holding a half-loaded file; drop it so the next call
            // starts clean.
            Release();
            return null;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidCastException or PlatformNotSupportedException)
        {
            // COM activation itself is unavailable — built-in COM interop switched off, or a
            // runtime configuration that cannot create the shell object. Every further call would
            // fail the same way, so report once and stay quiet.
            Console.WriteLine($"[AppDiscovery] Shortcut resolution unavailable: {ex.Message}");
            _unavailable = true;
            Release();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Release();
        _disposed = true;
    }

    private void Release()
    {
        if (_link == null)
            return;

        Marshal.FinalReleaseComObject(_link);
        _link = null;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath,
            IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxArgs);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int iconPathLength, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
#endif