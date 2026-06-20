namespace LoupixDeck.PluginSdk;

/// <summary>
/// A plain RGBA color. The SDK is UI-framework agnostic, so it does not expose
/// Avalonia or SkiaSharp color types — the host converts as needed.
/// </summary>
public readonly record struct PluginColor(byte R, byte G, byte B, byte A = 255)
{
    public static PluginColor Black => new(0, 0, 0);
    public static PluginColor White => new(255, 255, 255);
    public static PluginColor Transparent => new(0, 0, 0, 0);

    public static PluginColor FromRgb(byte r, byte g, byte b) => new(r, g, b);
}
