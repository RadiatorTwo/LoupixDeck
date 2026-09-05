using System.Diagnostics;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Grabs a single frame from a video file so a settings dialog can show what a clip looks like.
/// The wallpaper picker holds either a still image or a clip, and a picker whose preview goes blank
/// for half the things it can hold is not much of a picker.
///
/// This is a one-shot ffmpeg call, unrelated to <c>VideoFrameStream</c>: no pipe to keep fed, no
/// pacing, no ring. Callers are expected to cache the result — extracting is cheap but not free,
/// and a bound preview property is read far more often than the clip changes.
/// </summary>
public static class VideoPosterFrame
{
    /// <summary>How long to wait for the frame before giving up. Decoding one frame of an already
    /// open file is fast; a hang here would freeze the dialog, so the wait is bounded.</summary>
    private const int TimeoutMs = 4000;

    /// <summary>
    /// The clip's first frame, scaled to <paramref name="width"/>×<paramref name="height"/>, or null
    /// when the file is missing, ffmpeg is unavailable, or decoding failed. Never throws — a preview
    /// is a convenience and must not take the dialog down with it.
    /// </summary>
    public static SKBitmap Extract(string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (width <= 0 || height <= 0) return null;
        if (!FfmpegDetector.IsAvailable()) return null;

        try
        {
            // PNG on stdout rather than raw BGRA: SKBitmap.Decode then handles the framing, so a
            // short read cannot be mistaken for a valid frame.
            using var ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-v error -i \"{path}\" -frames:v 1 " +
                            $"-vf scale={width}:{height} -f image2pipe -vcodec png -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (ffmpeg == null) return null;

            // Drain stderr on a separate thread: a full stderr pipe would block ffmpeg forever
            // while we sit reading stdout.
            var drain = Task.Run(() => ffmpeg.StandardError.ReadToEnd());

            using var buffer = new MemoryStream();
            ffmpeg.StandardOutput.BaseStream.CopyTo(buffer);

            if (!ffmpeg.WaitForExit(TimeoutMs))
            {
                try { ffmpeg.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }

            drain.Wait(500);

            if (buffer.Length == 0) return null;

            buffer.Position = 0;
            return SKBitmap.Decode(buffer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VideoPosterFrame: could not read a frame from '{path}': {ex.Message}");
            return null;
        }
    }
}
