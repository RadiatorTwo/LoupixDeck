using System.Buffers;
using System.Runtime.InteropServices;
using LoupixDeck.Services.Animation;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Decodes an animated image (GIF / animated WebP, or any single still SkiaSharp can read) into a
/// flat <see cref="DecodedAnimation"/> of fully composited BGRA frames using <see cref="SKCodec"/>.
/// This runs ONCE per asset (driven by <see cref="LoupixDeck.Services.Animation.IAnimatedImageCache"/>);
/// playback then just blits the pre-decoded frames, so no ffmpeg process is ever spawned per button
/// at runtime — which is the whole point of normalizing imports to a frame source SkiaSharp can read.
///
/// GIF/WebP frames are commonly deltas over a prior frame (<see cref="SKCodecFrameInfo.RequiredFrame"/>
/// + a disposal method). We reconstruct each final frame by decoding on top of its required frame
/// (Skia applies the required frame's disposal internally) and snapshot the composited result, so
/// the produced frames are independent full images.
/// </summary>
public static class AnimatedImageDecoder
{
    /// <summary>Hard cap on decoded frames so a pathological clip can't exhaust memory.</summary>
    public const int DefaultMaxFrames = 300;

    /// <summary>Fallback duration for frames that declare none (or a non-positive one).</summary>
    private const int FallbackFrameMs = 100;

    /// <summary>
    /// The shorter edge frames are decoded down to. The tallest surface an animation fills is a
    /// 270 px side strip (the CT wheel is 240 px), and a layer covers it by scaling its fitted image
    /// up, so a frame whose shorter edge is this long is still drawn at full resolution there. The
    /// cache keeps every frame for the whole session, so a large source kept at its own size cost
    /// hundreds of megabytes for no visible gain.
    /// </summary>
    public const int MaxFrameShortEdge = 270;

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

            SKImageInfo frameInfo = FrameInfoFor(info);

            if (!animated)
                return DecodeStill(codec, info, frameInfo);

            var count = Math.Min(rawCount, maxFrames > 0 ? maxFrames : rawCount);
            var frames = new SKBitmap[count];
            var durations = new int[count];

            // Persistent decode buffer: holds the most recently decoded frame so the common
            // sequential-dependency case needs no pixel copy.
            using var canvas = new SKBitmap(info);
            var lastIndex = -1;

            // Frames are decoded at the source size, since each may be a delta drawn over an earlier
            // one, and only the stored copy is downscaled. A frame that has to be restored later then
            // needs a full-size copy of its own: a frame the codec asks for out of order, and the
            // last good frame a failed decode falls back to.
            bool downscale = frameInfo.Width != info.Width || frameInfo.Height != info.Height;
            HashSet<int> restoredLater = downscale ? OutOfOrderRequiredFrames(frameInfos, count) : [];
            Dictionary<int, SKBitmap> fullSize = [];
            using SKBitmap lastGood = downscale ? new SKBitmap(info) : null;

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
                else if (required < i && FullSizeFrame(frames, fullSize, downscale, required) is { } restore)
                {
                    // Non-sequential dependency: restore the required frame into the buffer first.
                    CopyPixels(restore, canvas, info);
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
                    SKBitmap previous = i == 0 ? null : downscale ? lastGood : frames[i - 1];
                    if (previous != null)
                        CopyPixels(previous, canvas, info);
                }

                lastIndex = i;
                // Playback only draws the frames; immutable spares Skia a full copy per draw.
                frames[i] = downscale ? Downscale(canvas, frameInfo) : canvas.Copy();
                frames[i]?.SetImmutable();
                durations[i] = fi.Duration > 0 ? fi.Duration : FallbackFrameMs;

                if (downscale)
                {
                    CopyPixels(canvas, lastGood, info);
                    if (restoredLater.Contains(i))
                        fullSize[i] = canvas.Copy();
                }
            }

            foreach (SKBitmap copy in fullSize.Values)
                copy.Dispose();

            // Button animations loop by default (issue #121 v1).
            return new DecodedAnimation(frames, durations, loops: true, info.Width, info.Height);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AnimatedImageDecoder: failed to decode '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    private static DecodedAnimation DecodeStill(SKCodec codec, SKImageInfo info, SKImageInfo frameInfo)
    {
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            return null;
        }

        if (frameInfo.Width != info.Width || frameInfo.Height != info.Height)
        {
            SKBitmap scaled = Downscale(bmp, frameInfo);
            bmp.Dispose();
            bmp = scaled;
        }

        bmp.SetImmutable();

        return new DecodedAnimation(new[] { bmp }, new[] { FallbackFrameMs },
            loops: false, info.Width, info.Height);
    }

    /// <summary>
    /// The size the frames of a <paramref name="source"/>-sized image are kept at: its own, or scaled
    /// down so the shorter edge is <see cref="MaxFrameShortEdge"/>.
    /// </summary>
    private static SKImageInfo FrameInfoFor(SKImageInfo source)
    {
        int shortEdge = Math.Min(source.Width, source.Height);
        if (shortEdge <= MaxFrameShortEdge) return source;

        double scale = MaxFrameShortEdge / (double)shortEdge;
        return source.WithSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    /// <summary>A copy of <paramref name="source"/> at <paramref name="frameInfo"/>'s size that remembers the source size.</summary>
    private static SKBitmap Downscale(SKBitmap source, SKImageInfo frameInfo)
    {
        var frame = new DownscaledBitmap(frameInfo, source.Width, source.Height);
        source.ScalePixels(frame, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return frame;
    }

    /// <summary>The frames a later frame builds on that are not the frame right before it.</summary>
    private static HashSet<int> OutOfOrderRequiredFrames(SKCodecFrameInfo[] frameInfos, int count)
    {
        HashSet<int> result = [];
        for (int i = 0; i < count; i++)
        {
            int required = frameInfos[i].RequiredFrame;
            if (required >= 0 && required < i - 1)
                result.Add(required);
        }

        return result;
    }

    /// <summary>Frame <paramref name="index"/> at the source size, or null when there is none.</summary>
    private static SKBitmap FullSizeFrame(SKBitmap[] frames, Dictionary<int, SKBitmap> fullSize, bool downscale, int index)
    {
        if (!downscale) return frames[index];
        return fullSize.TryGetValue(index, out SKBitmap copy) ? copy : null;
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
