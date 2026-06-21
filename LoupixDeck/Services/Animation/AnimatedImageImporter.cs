using System.Collections.Immutable;
using System.Diagnostics;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Imports an animated source file for use on a button and returns the stored asset relative path.
/// The decode-once contract (issue #121): GIF / animated WebP are stored verbatim (SkiaSharp reads
/// them directly — no ffmpeg). A video (MP4/MOV/…) is transcoded ONCE here, at import time, into a
/// small button-size looping GIF; runtime playback then only blits the pre-decoded frames, so no
/// ffmpeg process is ever spawned per button while the deck is running.
/// </summary>
public interface IAnimatedImageImporter
{
    /// <summary>True when video import is possible (ffmpeg on PATH). GIF/WebP import never needs it.</summary>
    bool IsVideoImportAvailable { get; }

    /// <summary>File extensions accepted as already-animated images (no transcode).</summary>
    IReadOnlyCollection<string> AnimatedImageExtensions { get; }

    /// <summary>File extensions accepted as video (transcoded once on import).</summary>
    IReadOnlyCollection<string> VideoExtensions { get; }

    /// <summary>
    /// Imports <paramref name="sourcePath"/> and returns the stored asset relative path
    /// (e.g. <c>assets/animations/&lt;hash&gt;.gif</c>), or null on failure / unsupported type /
    /// missing ffmpeg for a video. Runs the transcode (if any) on a background thread.
    /// </summary>
    Task<string> ImportAsync(string sourcePath);
}

/// <inheritdoc cref="IAnimatedImageImporter"/>
public sealed class AnimatedImageImporter(IAssetService assetService) : IAnimatedImageImporter
{
    private const string AnimationsSubFolder = "animations";

    // Button-size normalization. The deck button is 90×90; cap frame rate and length so the
    // imported GIF stays small and decodes to a bounded number of frames.
    private const int ButtonSize = 90;
    private const int ImportFps = 15;
    private const int MaxSeconds = 10;

    private static readonly ImmutableArray<string> _animatedImageExt = [ ".gif", ".webp" ];
    private static readonly ImmutableArray<string> _videoExt = [ ".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v", ".gifv" ];

    public bool IsVideoImportAvailable => FfmpegDetector.IsAvailable();
    public IReadOnlyCollection<string> AnimatedImageExtensions => _animatedImageExt;
    public IReadOnlyCollection<string> VideoExtensions => _videoExt;

    public async Task<string> ImportAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Already an animated image SkiaSharp can read — store as-is, no ffmpeg.
        if (_animatedImageExt.Contains(ext))
            return assetService.Import(sourcePath, AnimationsSubFolder);

        if (_videoExt.Contains(ext))
            return await ImportVideoAsync(sourcePath).ConfigureAwait(false);

        // Unknown extension: try as a still/animated image SkiaSharp might still read.
        return assetService.Import(sourcePath, AnimationsSubFolder);
    }

    private async Task<string> ImportVideoAsync(string sourcePath)
    {
        if (!FfmpegDetector.IsAvailable())
        {
            Console.WriteLine("[AnimatedImport] ffmpeg not found on PATH — video import unavailable.");
            return null;
        }

        var tempGif = Path.Combine(Path.GetTempPath(),
            "loupix_anim_" + Guid.NewGuid().ToString("N") + ".gif");

        try
        {
            // Single transcode to a button-size looping GIF. palettegen/paletteuse give acceptable
            // quality at small size; force_original_aspect_ratio + pad keep the square button frame.
            var vf =
                $"fps={ImportFps},scale={ButtonSize}:{ButtonSize}:flags=lanczos:force_original_aspect_ratio=decrease," +
                $"pad={ButtonSize}:{ButtonSize}:(ow-iw)/2:(oh-ih)/2,split[s0][s1];" +
                "[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer";

            var args =
                $"-hide_banner -loglevel error -t {MaxSeconds} -i \"{sourcePath}\" " +
                $"-vf \"{vf}\" -loop 0 -y \"{tempGif}\"";

            var ok = await RunFfmpegAsync(args).ConfigureAwait(false);
            if (!ok || !File.Exists(tempGif))
            {
                Console.WriteLine("[AnimatedImport] ffmpeg transcode failed.");
                return null;
            }

            return assetService.Import(tempGif, AnimationsSubFolder);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnimatedImport] video import failed: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(tempGif)) File.Delete(tempGif); } catch { /* best effort */ }
        }
    }

    private static async Task<bool> RunFfmpegAsync(string args)
    {
        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnimatedImport] ffmpeg start failed: {ex.Message}");
            return false;
        }

        if (process == null) return false;

        // Drain stderr so the pipe never stalls the process.
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var err = await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(err))
            Console.WriteLine($"[AnimatedImport] ffmpeg: {err.Trim()}");

        var code = process.ExitCode;
        process.Dispose();
        return code == 0;
    }
}
