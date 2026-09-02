using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Setup;

/// <summary>
/// Makes the setup a genuinely single distributable exe despite Avalonia's native renderer
/// dependencies (Skia / HarfBuzz / ANGLE). Rather than embedding a second copy of those DLLs, we pull
/// them out of the embedded <c>payload.zip</c> (which already ships them at its root next to
/// <c>LoupixDeck.exe</c>) — the setup and the app must resolve the same SkiaSharp/HarfBuzzSharp
/// (direct CPM refs on both projects). The libs are extracted to a per-version temp folder and pinned
/// via a <see cref="NativeLibrary"/> resolver before Avalonia initializes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeBootstrap
{
    private const string PayloadResourceName = "payload.zip";

    /// <summary>File-name prefixes of the native renderer libs to hoist out of the payload root.</summary>
    private static readonly string[] NativePrefixes = ["libSkiaSharp", "libHarfBuzzSharp", "av_"];

    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    /// <summary>Folder the native libs were extracted to; used by the DllImport resolver.</summary>
    private static string? _nativeDir;

    // [LibraryImport] requires the exact export name — no automatic A/W suffixing like [DllImport].
    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string lpPathName);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    public static void Prepare()
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using Stream? payload = asm.GetManifestResourceStream(PayloadResourceName);
            if (payload == null)
                return; // dev build without a payload: native libs sit beside the exe (bin/publish)

            string version = asm.GetName().Version?.ToString() ?? "0";
            string dir = Path.Combine(Path.GetTempPath(), "LoupixDeck.Setup", "native-" + version);
            Directory.CreateDirectory(dir);

            List<string> extracted = ExtractPayloadNatives(payload, dir);
            if (extracted.Count == 0)
                return;

            _nativeDir = dir;

            // Put our folder on the search path for dependency resolution (e.g. ANGLE, which Avalonia
            // loads itself)…
            SetDllDirectory(dir);
            foreach (string path in extracted)
            {
                IntPtr handle = LoadLibraryExW(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (handle == IntPtr.Zero &&
                    string.Equals(Path.GetFileName(path), "libSkiaSharp.dll", StringComparison.OrdinalIgnoreCase))
                {
                    int err = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException(
                        $"Failed to load libSkiaSharp.dll from '{path}' (Win32 {err}).");
                }
            }

            // …and, crucially, force SkiaSharp/HarfBuzzSharp to bind OUR exact copies. A name-only
            // P/Invoke for "libSkiaSharp" can otherwise resolve to an unrelated build found earlier on
            // the search path (observed: a stray v88 vs. our required v119), throwing a version
            // mismatch. A DllImportResolver on the declaring assembly wins over the default search.
            RegisterResolver(typeof(SkiaSharp.SKImageInfo).Assembly);
            RegisterResolver(typeof(HarfBuzzSharp.Blob).Assembly);
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "loupix-setup-bootstrap.log"),
                    ex + Environment.NewLine);
            }
            catch { }

            throw;
        }
    }

    /// <summary>Hoists root-level renderer libs out of the embedded payload zip.</summary>
    private static List<string> ExtractPayloadNatives(Stream payload, string dir)
    {
        List<string> extracted = new();
        using ZipArchive archive = new(payload, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = entry.FullName.Replace('\\', '/');
            // Only root-level native libs (skip anything nested, e.g. plugins/...).
            if (relative.Contains('/') || !IsNativeLib(entry.Name))
                continue;

            string target = Path.Combine(dir, entry.Name);
            if (!(File.Exists(target) && new FileInfo(target).Length == entry.Length))
                entry.ExtractToFile(target, overwrite: true);

            extracted.Add(target);
        }

        return extracted;
    }

    private static bool IsNativeLib(string fileName)
    {
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string prefix in NativePrefixes)
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void RegisterResolver(Assembly assembly)
    {
        NativeLibrary.SetDllImportResolver(assembly, static (name, _, _) =>
        {
            if (_nativeDir == null)
                return IntPtr.Zero;

            string? file = name switch
            {
                "libSkiaSharp" => "libSkiaSharp.dll",
                "libHarfBuzzSharp" => "libHarfBuzzSharp.dll",
                _ => null
            };
            if (file == null)
                return IntPtr.Zero;

            string full = Path.Combine(_nativeDir, file);
            return File.Exists(full) && NativeLibrary.TryLoad(full, out IntPtr handle)
                ? handle
                : IntPtr.Zero;
        });
    }
}
