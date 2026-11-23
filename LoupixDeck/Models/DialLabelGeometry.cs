namespace LoupixDeck.Models;

/// <summary>
/// Size and layout constants of the Live S dial labels (<see cref="RotaryButton.DialLabel"/>).
/// The pixel values are for a 90 px key and are scaled to the device's key size.
/// </summary>
public static class DialLabelGeometry
{
    private const int ReferenceKeySizePx = 90;

    /// <summary>Width of a label on the reference key; its height is the key's.</summary>
    private const int WidthPx = 24;

    /// <summary>Box and font size of a new text layer on a label: narrow enough to stack the letters.</summary>
    private const int TextBoxWidthPx = 10;
    private const int TextBoxHeightPx = 80;
    private const int TextSizePx = 10;

    public static int Width(int keySize) => Scale(WidthPx, keySize);

    public static int TextBoxWidth(int keySize) => Scale(TextBoxWidthPx, keySize);

    public static int TextBoxHeight(int keySize) => Scale(TextBoxHeightPx, keySize);

    public static int TextSize(int keySize) => Scale(TextSizePx, keySize);

    /// <summary>
    /// How the layers of <paramref name="key"/> are drawn on a key of <paramref name="keySize"/> pixels:
    /// on the part of the key beside its dial label while the labels are shown with
    /// <see cref="LoupedeckConfig.DialLabelsKeepKeysClear"/>, otherwise on the whole key.
    /// </summary>
    public static KeyInset KeyInsetFor(LoupedeckConfig config, TouchButton key, int keySize)
    {
        if (config is not { DialLabelsEnabled: true, DialLabelsKeepKeysClear: true } || key == null ||
            key.IsFolderNavTile || config.DialLabelSlots?.Contains(key.Index) != true)
            return default;

        return new KeyInset(Width(keySize));
    }

    /// <summary>Width of the surface the layers of <paramref name="key"/> are laid out on.</summary>
    public static int LayerSurfaceWidth(LoupedeckConfig config, TouchButton key, int keySize) =>
        Math.Max(1, keySize - KeyInsetFor(config, key, keySize).Left);

    private static int Scale(int px, int keySize) =>
        Math.Max(1, (int)Math.Round(px * (keySize > 0 ? keySize : ReferenceKeySizePx) / (double)ReferenceKeySizePx));
}
