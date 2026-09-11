using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// Puts a command onto a touch button the way the actions panel offers it: the command itself, its
/// Material Design glyph, and the action's name as a caption underneath.
/// </summary>
/// <remarks>
/// The sibling of <see cref="AppLauncher.AppAssignment"/> for everything that is not an installed
/// application. A command has no artwork of its own, so the button is built from the glyph the
/// command declares (<c>CommandAttribute.Icon</c>, surfaced through <c>MenuEntry.Icon</c>) plus a
/// text caption. Commands that declare no glyph get the caption alone, at a larger size.
/// </remarks>
public static class ActionAssignment
{
    /// <summary>
    /// Key size the layout constants below are expressed in. Every pixel value is scaled from this
    /// reference onto the key actually being written, because key size is per-device and
    /// user-calibratable.
    /// </summary>
    private const int ReferenceKeySizePx = 90;

    /// <summary>Rendered size of the glyph on the reference key.</summary>
    private const int SymbolSizePx = 46;

    /// <summary>Glyph offset from the key centre, negative being upwards — it sits above the caption.</summary>
    private const int SymbolOffsetYPx = -8;

    /// <summary>Caption font size, offset from the key centre, and layout box, all on the reference key.</summary>
    private const int LabelTextSizePx = 11;
    private const int LabelOffsetYPx = 27;
    private const int LabelBoxWidthPx = 88;
    private const int LabelBoxHeightPx = 22;

    /// <summary>Caption metrics for a command with no glyph, where the text owns the whole key.</summary>
    private const int TextOnlySizePx = 14;
    private const int TextOnlyBoxPx = 84;

    /// <summary>
    /// The fraction of the key's short edge the glyph fills. A <see cref="LayerBase.Scale"/> is
    /// already relative to the surface, so unlike the pixel constants it needs no scaling.
    /// </summary>
    private const double SymbolScale = SymbolSizePx / (double)ReferenceKeySizePx;

    /// <summary>
    /// Assigns <paramref name="command"/> to <paramref name="button"/> and rebuilds its artwork from
    /// <paramref name="label"/> and <paramref name="symbolId"/>. Existing layers are replaced: an
    /// action brings its own complete look, and the caller confirms the replacement beforehand.
    /// </summary>
    /// <param name="symbolId">
    /// A <see cref="SymbolLibrary"/> id, or null. A glyph the library does not know is treated as
    /// absent rather than as an error, so a command declaring an icon we cannot resolve still
    /// lands on the button as a caption.
    /// </param>
    /// <param name="keyWidthPx">Width of the key being written, in device pixels.</param>
    /// <param name="keyHeightPx">Height of the key being written, in device pixels.</param>
    public static void ApplyToTouchButton(TouchButton button, string command, string label,
        string symbolId, int keyWidthPx, int keyHeightPx)
    {
        if (button == null || string.IsNullOrEmpty(command))
            return;

        button.Command = command;
        button.Layers.Clear();

        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);
        string text = label ?? string.Empty;

        if (!string.IsNullOrEmpty(symbolId) && SymbolLibrary.TryGet(symbolId, out _))
        {
            button.Layers.Add(new SymbolLayer
            {
                Name = text,
                SymbolId = symbolId,
                Scale = SymbolScale,
                PositionY = Scaled(SymbolOffsetYPx, scaleY)
            });

            button.Layers.Add(new TextLayer
            {
                Name = text,
                Text = text,
                Centered = true,
                TextSize = Scaled(LabelTextSizePx, scaleY),
                PositionY = Scaled(LabelOffsetYPx, scaleY),
                BoxWidth = Scaled(LabelBoxWidthPx, scaleX),
                BoxHeight = Scaled(LabelBoxHeightPx, scaleY)
            });
        }
        else
        {
            button.Layers.Add(new TextLayer
            {
                Name = text,
                Text = text,
                Centered = true,
                TextSize = Scaled(TextOnlySizePx, scaleY),
                BoxWidth = Scaled(TextOnlyBoxPx, scaleX),
                BoxHeight = Scaled(TextOnlyBoxPx, scaleY)
            });
        }

        button.RewireLayerHandlers();
    }

    /// <summary>
    /// Maps the reference key's pixel space onto a key of <paramref name="edgePx"/> pixels. A key
    /// size that is not known yet falls back to 1.0 rather than collapsing every layer onto a point.
    /// </summary>
    private static double ScaleFactor(int edgePx)
        => edgePx <= 0 ? 1.0 : edgePx / (double)ReferenceKeySizePx;

    /// <summary>Rounds a reference-key pixel value onto the real key, never below one pixel of
    /// magnitude so an offset or a box does not vanish on a small key.</summary>
    private static int Scaled(int referencePx, double factor)
    {
        int value = (int)Math.Round(referencePx * factor);
        if ((value == 0) && (referencePx != 0))
            return referencePx < 0 ? -1 : 1;

        return value;
    }
}
