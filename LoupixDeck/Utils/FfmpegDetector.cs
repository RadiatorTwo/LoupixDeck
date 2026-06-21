using System.Diagnostics;

namespace LoupixDeck.Utils;

/// <summary>
/// Detects whether an <c>ffmpeg</c> binary is reachable on the system PATH. ffmpeg is
/// not bundled with the app (issue #120); the screensaver feature requires it to decode
/// GIF/MP4. The probe result is cached after the first call so repeated checks (idle
/// ticks, settings UI) are cheap.
/// </summary>
public static class FfmpegDetector
{
    private static readonly Lock Gate = new();
    private static bool? _available;

    /// <summary>True when <c>ffmpeg -version</c> can be launched and exits cleanly.</summary>
    public static bool IsAvailable()
    {
        lock (Gate)
        {
            _available ??= Probe();
            return _available.Value;
        }
    }

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return false;

            if (!process.WaitForExit(3000))
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // ffmpeg not on PATH, or launching it failed → feature unavailable.
            return false;
        }
    }
}
