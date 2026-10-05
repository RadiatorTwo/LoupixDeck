#if !WINDOWS
using System.Runtime.InteropServices;
using LoupixDeck.Services.Diagnostics.Linux;

namespace LoupixDeck.Utils;

/// <summary>
/// Where the single-instance CLI socket lives on Linux and macOS.
/// </summary>
/// <remarks>
/// The socket used to sit at the shared <c>/tmp/loupixdeck_app.sock</c>, so a second user on
/// the same machine either found the first user's socket and exited, or could not remove it.
/// Each user now gets their own, in a directory only they can enter, readable and writable
/// only by them.
///
/// The running app and a CLI invocation must arrive at the same path, so the location does not
/// depend on environment that a cron job or an ssh session may lack where that can be avoided.
/// </remarks>
internal static partial class CliSocket
{
    private const string FileName = "loupixdeck_app.sock";

    private static readonly Lazy<string> ResolvedPath = new(Resolve);

    /// <summary>The socket path. Throws <see cref="IOException"/> when no private directory
    /// can be found or made.</summary>
    public static string Path => ResolvedPath.Value;

    /// <summary>Restricts the bound socket to its owner. Called straight after bind.</summary>
    public static void Restrict()
    {
        // This file only compiles without WINDOWS; the analyzer needs the guard spelled out.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Resolve()
    {
        string directory = OperatingSystem.IsMacOS() ? MacUserTempDirectory() : LinuxRuntimeDirectory();

        return System.IO.Path.Combine(directory, FileName);
    }

    // ──────── macOS ────────

    private const int CsDarwinUserTempDir = 65537;

    [LibraryImport("libc", EntryPoint = "confstr")]
    private static partial nuint Confstr(int name, Span<byte> buffer, nuint length);

    /// <summary>
    /// The per-user /var/folders/…/T/ directory (mode 0700). $TMPDIR is set from the same value,
    /// but only in a login session; asking the system directly also works from cron or ssh.
    /// </summary>
    private static string MacUserTempDirectory()
    {
        try
        {
            Span<byte> buffer = stackalloc byte[1024];
            nuint length = Confstr(CsDarwinUserTempDir, buffer, (nuint)buffer.Length);

            if (length > 1 && length <= (nuint)buffer.Length)
            {
                // The length includes the terminating NUL.
                return System.Text.Encoding.UTF8.GetString(buffer[..((int)length - 1)]);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI] confstr failed: {ex.Message}");
        }

        return System.IO.Path.GetTempPath();
    }

    // ──────── Linux ────────

    /// <summary>
    /// $XDG_RUNTIME_DIR, or /run/user/&lt;uid&gt; when the variable is missing (cron), and only
    /// when the session manager has made that directory. Otherwise a 0700 directory of our own
    /// under /tmp.
    /// </summary>
    private static string LinuxRuntimeDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        string runtime = LinuxSystemFacts.RuntimeDirectory();

        if (!string.IsNullOrWhiteSpace(runtime) && Directory.Exists(runtime))
        {
            return runtime;
        }

        int? uid = LinuxSystemFacts.EffectiveUserId();
        string fallback = $"/tmp/loupixdeck-{uid?.ToString() ?? Environment.UserName}";
        const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        // /tmp is shared: another user can create this name first. Only a directory that is
        // exactly 0700 is ours to use; anything else may be someone else's and is refused.
        Directory.CreateDirectory(fallback, privateMode);

        if (File.GetUnixFileMode(fallback) != privateMode)
        {
            throw new IOException($"{fallback} is not private (mode 0700); it may belong to another user.");
        }

        return fallback;
    }
}
#endif
