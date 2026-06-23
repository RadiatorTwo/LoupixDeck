using System.Buffers;
using System.Runtime.InteropServices;
using LoupixDeck.Services.Animation;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Decodes an animated image (GIF / animated WebP, or any single still SkiaSharp can read) into a
/// flat <see cref="DecodedAnimation"/> of fully composited BGRA frames using <see cref="SKCodec"/>.
/// </summary>
/// <remarks>
/// <para>
/// GIF/WebP frames are commonly deltas over a prior frame (<see cref="SKCodecFrameInfo.RequiredFrame"/>
/// + a disposal method). We reconstruct each final frame by decoding on top of its required frame
/// (Skia applies the required frame's disposal internally) and snapshot the composited result, so
/// the produced frames are independent full images.
/// </para>
/// <para>
/// This runs ONCE per asset (driven by <see cref="LoupixDeck.Services.Animation.IAnimatedImageCache"/>);
/// playback then just blits the pre-decoded frames, so no ffmpeg process is ever spawned per button
/// at runtime — which is the whole point of normalizing imports to a frame source SkiaSharp can read.
/// </para>
/// </remarks>
public static class AnimatedImageDecoder
{
    /// <summary>Hard cap on decoded frames so a pathological clip can't exhaust memory.</summary>
    public const int DefaultMaxFrames = 300;

    /// <summary>Fallback duration for frames that declare none (or a non-positive one).</summary>
    private const int FallbackFrameMs = 100;

    /// <summary>
    /// Decodes <paramref name="absolutePath"/>. Returns null when the file is missing or unreadable.
    /// A single-frame image yields a one-frame, non-looping animation.
    /// </summary>
    public static DecodedAnimation Decode(string absolutePath, int maxFrames = DefaultMaxFrames)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return null;

        try
        {
            // Decode from a stream we control so the codec never holds the file open afterwards.
            using var stream = File.OpenRead(absolutePath);
            using var managed = new SKManagedStream(stream, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed);
            if (codec == null) return null;

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);
            if (info.Width <= 0 || info.Height <= 0) return null;

            var rawCount = Math.Max(1, codec.FrameCount);
            var frameInfos = codec.FrameInfo;
            var animated = rawCount > 1 && frameInfos is { Length: > 0 } && frameInfos.Length >= rawCount;

            if (!animated)
                return DecodeStill(codec, info);

            var count = Math.Min(rawCount, maxFrames > 0 ? maxFrames : rawCount);
            var frames = new SKBitmap[count];
            var durations = new int[count];

            // Persistent decode buffer: holds the most recently decoded frame so the common
            // sequential-dependency case needs no pixel copy.
            using var canvas = new SKBitmap(info);
            var lastIndex = -1;

            for (var i = 0; i < count; i++)
            {
                var fi = frameInfos[i];
                var required = fi.RequiredFrame;

                SKCodecOptions options;
                if (required < 0)
                {
                    // Independent frame: start from a clean (transparent) buffer.
                    canvas.Erase(SKColors.Transparent);
                    options = new SKCodecOptions(i);
                }
                else if (required == lastIndex)
                {
                    // Buffer already holds the required frame — decode straight on top of it.
                    options = new SKCodecOptions(i, required);
                }
                else if (required < i && frames[required] != null)
                {
                    // Non-sequential dependency: restore the required frame into the buffer first.
                    CopyPixels(frames[required], canvas, info);
                    options = new SKCodecOptions(i, required);
                }
                else
                {
                    // Can't satisfy the dependency — best-effort independent decode.
                    canvas.Erase(SKColors.Transparent);
                    options = new SKCodecOptions(i);
                }

                var result = codec.GetPixels(info, canvas.GetPixels(), options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    // Decode failed for this frame: hold the previous frame (or transparent).
                    if (i > 0 && frames[i - 1] != null)
                        CopyPixels(frames[i - 1], canvas, info);
                }

                lastIndex = i;
                frames[i] = canvas.Copy();
                durations[i] = fi.Duration > 0 ? fi.Duration : FallbackFrameMs;
            }

            // Button animations loop by default (issue #121 v1).
            return new DecodedAnimation(frames, durations, loops: true, info.Width, info.Height);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AnimatedImageDecoder: failed to decode '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    private static DecodedAnimation DecodeStill(SKCodec codec, SKImageInfo info)
    {
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            return null;
        }

        return new DecodedAnimation(new[] { bmp }, new[] { FallbackFrameMs },
            loops: false, info.Width, info.Height);
    }

    /// <summary>Copies the pixels of <paramref name="src"/> into <paramref name="dst"/> (same info).</summary>
    private static void CopyPixels(SKBitmap src, SKBitmap dst, SKImageInfo info)
    {
        var len = info.BytesSize;
        var buffer = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            Marshal.Copy(src.GetPixels(), buffer, 0, len);
            Marshal.Copy(buffer, 0, dst.GetPixels(), len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
