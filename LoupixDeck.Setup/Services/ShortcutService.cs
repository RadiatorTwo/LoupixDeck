using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Creates and removes Windows <c>.lnk</c> shortcuts via the Shell <c>IShellLink</c> COM object.
/// Uses source-generated COM interop (<see cref="GeneratedComInterfaceAttribute"/> +
/// <see cref="StrategyBasedComWrappers"/>) so it stays NativeAOT-compatible.
/// </summary>
public static partial class ShortcutService
{
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0;

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext,
        in Guid riid, out nint ppv);

    /// <summary>
    /// Creates (or overwrites) a shortcut file targeting <paramref name="targetExe"/>.
    /// </summary>
    public static void Create(string shortcutPath, string targetExe, string? iconPath = null,
        string? description = null, string? arguments = null)
    {
        // Shell COM tolerates MTA; ignore S_FALSE / already-initialized results.
        CoInitializeEx(0, COINIT_MULTITHREADED);

        string? dir = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        int hr = CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out nint pUnk);
        Marshal.ThrowExceptionForHR(hr);

        StrategyBasedComWrappers wrappers = new();
        try
        {
            IShellLinkW link = (IShellLinkW)wrappers.GetOrCreateObjectForComInstance(pUnk, CreateObjectFlags.None);

            link.SetPath(targetExe);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? "");
            if (!string.IsNullOrEmpty(iconPath))
                link.SetIconLocation(iconPath, 0);
            if (!string.IsNullOrEmpty(description))
                link.SetDescription(description);
            if (!string.IsNullOrEmpty(arguments))
                link.SetArguments(arguments);

            IPersistFile file = (IPersistFile)link;
            file.Save(shortcutPath, true);
        }
        finally
        {
            Marshal.Release(pUnk);
        }
    }

    /// <summary>Deletes a shortcut file if it exists.</summary>
    public static void Remove(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
        }
        catch
        {
            // best effort
        }
    }

    // ── COM interfaces (vtable order matters; unused slots are placeholders) ──

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
        void GetIDList(out nint ppidl);
        void SetIDList(nint pidl);
        void GetDescription(nint pszName, int cch);
        void SetDescription(string pszName);
        void GetWorkingDirectory(nint pszDir, int cch);
        void SetWorkingDirectory(string pszDir);
        void GetArguments(nint pszArgs, int cch);
        void SetArguments(string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint dwReserved);
        void Resolve(nint hwnd, uint fFlags);
        void SetPath(string pszFile);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(string pszFileName, uint dwMode);
        void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted(string pszFileName);
        void GetCurFile(out string ppszFileName);
    }
}
