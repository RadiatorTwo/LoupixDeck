using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

/// <summary>
/// The look new text and icon layers start with (issue #564), set in Settings. Global like the default
/// font: stored in <c>layer-defaults.json</c> next to the device configs, never in a device config.
/// Each group is off until the user turns it on, so the built-in look stays as it was.
/// </summary>
public sealed class LayerDefaultSettings
{
    public TextLayerDefaults Text { get; set; } = new();

    public IconLayerDefaults Icon { get; set; } = new();
}

/// <summary>
/// Defaults for new text layers. Sizes and positions are pixels on the 90 px reference key and are
/// scaled onto the key a layer is created for.
/// </summary>
public sealed partial class TextLayerDefaults : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial int TextSize { get; set; } = 11;

    /// <summary>Offset from the key centre; used where a text layer is added on its own in the button editor.</summary>
    [ObservableProperty]
    public partial int PositionX { get; set; }

    /// <inheritdoc cref="PositionX"/>
    [ObservableProperty]
    public partial int PositionY { get; set; }

    [ObservableProperty]
    public partial Color TextColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial bool Outlined { get; set; }

    [ObservableProperty]
    public partial Color OutlineColor { get; set; } = Colors.Black;
}

/// <summary>
/// Defaults for new symbol and image layers. Size and position apply to both; the colours, gradient,
/// outline and shadow only to symbols, which are the layers that have them.
/// </summary>
public sealed partial class IconLayerDefaults : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>The icon's longest edge as a percentage of the key's short edge.</summary>
    [ObservableProperty]
    public partial int SizePercent { get; set; } = 51;

    /// <summary>Offset from the key centre in reference-key pixels; used where an icon is added on its own in the button editor.</summary>
    [ObservableProperty]
    public partial int PositionX { get; set; }

    /// <inheritdoc cref="PositionX"/>
    [ObservableProperty]
    public partial int PositionY { get; set; }

    [ObservableProperty]
    public partial Color Tint { get; set; } = Colors.White;

    [ObservableProperty]
    public partial bool UseGradient { get; set; }

    [ObservableProperty]
    public partial Color GradientStartColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial Color GradientEndColor { get; set; } = Color.FromRgb(0x60, 0x60, 0x60);

    [ObservableProperty]
    public partial double GradientAngle { get; set; } = 90.0;

    [ObservableProperty]
    public partial bool Outlined { get; set; }

    [ObservableProperty]
    public partial Color OutlineColor { get; set; } = Colors.Black;

    [ObservableProperty]
    public partial double OutlineWidth { get; set; } = 3.0;

    [ObservableProperty]
    public partial bool Shadow { get; set; }
}
