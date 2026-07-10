using System.Collections.Concurrent;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Ordered (Bayer) dithering for the RGB888 to RGB565 downsample that every framebuffer
/// write goes through, quantizing against the panel's <em>real</em> channel depth rather
/// than against the depth the wire format suggests.
///
/// Measured on hardware with the colour-depth test pattern: the Loupedeck Live S resolves
/// all 32 red / 64 green / 32 blue levels RGB565 encodes, but the Razer Stream Controller
/// discards the low bit of red — its 32 red bars merge pairwise into 16, its adjacent-level
/// pairs (2,3), (4,5) … show no seam, and a 1-LSB red checkerboard reads as a coarse
/// pattern instead of a flat tone. Green and blue are unaffected on both devices.
///
/// That measurement is why dithering has to be depth-aware. Dithering into the 5-bit red
/// grid puts the entire pattern into the one bit the Razer throws away: half the decisions
/// vanish, the rest become full double-steps, and the result is grain with no smoothing.
/// Dithering into the panel's own 4-bit red grid survives the panel's quantization, and
/// the eye averages it back into intermediate tones.
///
/// Two properties hold for every (wire, panel) depth combination:
///
/// 1. Only the <em>residual</em> is dithered — the part of the 8-bit value the panel grid
///    cannot represent. A value sitting exactly on that grid has a zero residual and is
///    passed through with no pattern at all. <see cref="SnapToGrid"/> uses this to keep
///    flat text fills clean.
/// 2. The quantizer is unbiased: averaged over the Bayer matrix the reconstructed colour
///    matches the source to well under one level.
/// </summary>
public static class Rgb565Dither
{
    /// <summary>
    /// Classic 8x8 Bayer threshold matrix, values 0-63. Indexed by absolute display
    /// coordinates so the pattern stays continuous across separately-drawn regions
    /// (touch slots are 90px wide, which is not a multiple of 8 — indexing by a
    /// bitmap-local coordinate would shift the pattern phase at every slot seam).
    /// </summary>
    private static readonly byte[] Bayer8X8 =
    [
         0, 32,  8, 40,  2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44,  4, 36, 14, 46,  6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
         3, 35, 11, 43,  1, 33,  9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47,  7, 39, 13, 45,  5, 37,
        63, 31, 55, 23, 61, 29, 53, 21,
    ];

    /// <summary>Number of Bayer threshold levels.</summary>
    public const int Thresholds = 64;

    /// <summary>Threshold used to make the quantizer round to the nearest level.</summary>
    private const int RoundThreshold = Thresholds / 2;

    /// <summary>
    /// The coarsest channel depth across all supported devices (red 4 on the Razer Stream
    /// Controller, green 6 and blue 5 everywhere). <see cref="SnapToGrid"/> targets this so
    /// one snapped colour is grid-aligned on <em>every</em> device — a value on the 4-bit red
    /// grid also sits on the 5-bit grid, so the Live S keeps its text pattern-free too.
    /// Revisit if a device is added whose green or blue is coarser than this.
    /// </summary>
    private static readonly (int Red, int Green, int Blue) SnapBits = (4, 6, 5);

    /// <summary>Lookup tables keyed by (wireBits, panelBits). Each is 16 KiB and built once.</summary>
    private static readonly ConcurrentDictionary<(int Wire, int Panel), byte[]> Luts = new();

    /// <summary>Expands a 5-bit channel to 8 bits the way the panel does (bit replication).</summary>
    public static int Expand5(int level) => (level << 3) | (level >> 2);

    /// <summary>Expands a 6-bit channel to 8 bits (bit replication).</summary>
    public static int Expand6(int level) => (level << 2) | (level >> 4);

    private static int Expand(int level, int wireBits) => wireBits == 5 ? Expand5(level) : Expand6(level);

    /// <summary>Bayer threshold (0-63) at the given absolute display pixel.</summary>
    public static byte ThresholdAt(int x, int y) => Bayer8X8[((y & 7) << 3) | (x & 7)];

    /// <summary>
    /// Returns the dither table for a channel transmitted with <paramref name="wireBits"/>
    /// bits and actually resolved by the panel at <paramref name="panelBits"/> bits.
    /// Index it as <c>lut[(value * 64) + threshold]</c>; the result is the wire-level code to
    /// transmit. A direct lookup keeps the per-pixel cost to one indexed read, which matters
    /// for the full-screen animation path (129 600 pixels per frame).
    /// </summary>
    public static byte[] GetLut(int wireBits, int panelBits)
    {
        if (wireBits is not (5 or 6))
            throw new ArgumentOutOfRangeException(nameof(wireBits), wireBits, "RGB565 channels are 5 or 6 bits wide.");
        if (panelBits < 1 || panelBits > wireBits)
            throw new ArgumentOutOfRangeException(nameof(panelBits), panelBits, "Panel depth must be between 1 and wireBits.");

        return Luts.GetOrAdd((wireBits, panelBits), static key => BuildLut(key.Wire, key.Panel));
    }

    private static byte[] BuildLut(int wireBits, int panelBits)
    {
        // A panel that resolves panelBits of a wireBits-wide channel keeps the high bits and
        // discards the low `shift` ones, so panel level p is addressed by wire code p << shift
        // and reconstructs to Reconstruct(p).
        int shift = wireBits - panelBits;
        int maxLevel = (1 << panelBits) - 1;

        int Reconstruct(int level) => Expand(level << shift, wireBits);

        byte[] lut = new byte[256 * Thresholds];

        for (int value = 0; value < 256; value++)
        {
            // Largest panel level whose reconstruction is still <= value. The obvious
            // (value * maxLevel) / 255 only approximates that floor, so correct it in both
            // directions rather than trusting it.
            int level = (value * maxLevel) / 255;
            while (level < maxLevel && Reconstruct(level + 1) <= value) level++;
            while (level > 0 && Reconstruct(level) > value) level--;

            for (int threshold = 0; threshold < Thresholds; threshold++)
            {
                int dithered;
                if (level >= maxLevel)
                {
                    dithered = maxLevel;
                }
                else
                {
                    // residual == 0 exactly for grid-aligned values -> never bumps, so a flat
                    // fill of such a colour shows no dither pattern.
                    int residual = value - Reconstruct(level);
                    int span = Reconstruct(level + 1) - Reconstruct(level);
                    dithered = level + ((residual * Thresholds) > (threshold * span) ? 1 : 0);
                }

                // The panel level is placed in the high bits; the bits it discards stay zero.
                // For the Razer's red channel that yields exactly the R4-X1-G6-B5 layout
                // (bit 11 always clear). Hardware confirmed the panel ignores those bits, so
                // there is nothing to gain by setting them.
                lut[(value * Thresholds) + threshold] = (byte)(dithered << shift);
            }
        }

        return lut;
    }

    /// <summary>
    /// Snaps a colour onto the panel grid so a flat fill of it quantizes with a zero residual
    /// and therefore picks up no dither pattern.
    ///
    /// Used for text, whose glyph interiors are flat fills and where a 1-LSB noise pattern
    /// would read as grain rather than as a smoother gradient. Anti-aliased glyph edges blend
    /// between the snapped colour and the background and still dither, which is what keeps
    /// them looking smooth. Symbols, images and animations are left alone so their gradients
    /// benefit from the dithering.
    ///
    /// Targets <see cref="SnapBits"/> — the coarsest depth across devices — because the render
    /// happens before the bitmap reaches any particular device. The shift is at most one panel
    /// step and is not perceptible on its own.
    /// </summary>
    public static SKColor SnapToGrid(SKColor color) => new(
        SnapChannel(color.Red, 5, SnapBits.Red),
        SnapChannel(color.Green, 6, SnapBits.Green),
        SnapChannel(color.Blue, 5, SnapBits.Blue),
        color.Alpha);

    /// <summary>
    /// Snaps one channel to the nearest value that quantizes without a dither pattern.
    ///
    /// A value is pattern-free when its residual against the panel grid is zero — or when it
    /// saturates the top panel level, since the quantizer cannot bump past the maximum. The
    /// top level therefore keeps 255 rather than collapsing to the grid point below it: on the
    /// Razer that point is 247, and snapping white text down to it would tint white cyan while
    /// gaining nothing, as the panel shows both as its brightest red.
    /// </summary>
    private static byte SnapChannel(byte value, int wireBits, int panelBits)
    {
        int shift = wireBits - panelBits;
        int code = GetLut(wireBits, panelBits)[(value * Thresholds) + RoundThreshold];

        if ((code >> shift) >= (1 << panelBits) - 1)
            return 255;

        return (byte)Expand(code, wireBits);
    }
}
