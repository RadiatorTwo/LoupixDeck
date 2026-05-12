using Avalonia.Media;
using SkiaSharp;

namespace LoupixDeck.Services.FolderNavigation;

/// <summary>
/// One slot inside a folder view. Either invokes <see cref="OnPress"/> or opens
/// <see cref="OpensFolder"/> when the user taps the corresponding touch slot.
/// </summary>
public sealed class FolderEntry
{
    public int SlotIndex { get; init; }
    public string Text { get; init; }
    public SKBitmap Image { get; init; }
    public Color BackColor { get; init; } = Colors.Black;
    public Color TextColor { get; init; } = Colors.White;
    public int TextSize { get; init; } = 16;
    public bool Bold { get; init; }
    public Func<Task> OnPress { get; init; }
    public IFolderProvider OpensFolder { get; init; }
}

public sealed class RotaryOverride
{
    public Func<Task> OnLeft { get; init; }
    public Func<Task> OnRight { get; init; }
    public Func<Task> OnPress { get; init; }
}
