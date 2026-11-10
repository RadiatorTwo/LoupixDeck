using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

/// <summary>How a generated wallpaper background is painted.</summary>
public enum WallpaperFillMode
{
    Solid,
    Linear,
    Radial,
}

/// <summary>
/// A background generated from colours instead of an image (issue #577): a solid colour or a linear
/// or radial gradient, optionally with a middle colour, a vignette and a little noise. It belongs to
/// a <see cref="WallpaperSlot"/> and is baked into the slot's bitmap like an image would be, so it is
/// drawn, cut out per key and inherited by later pages exactly like an image wallpaper.
///
/// The slot only paints it while it holds no image or clip; picking one keeps these settings, so
/// removing the image brings the generated background back. Absent from a config saved before this
/// existed, which loads as a disabled fill and so behaves exactly as before.
/// </summary>
[ObservableObject]
public partial class WallpaperFill
{
    /// <summary>Whether the slot paints this background when it has no image or clip.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }
    partial void OnEnabledChanged(bool value) => RaiseChanged();

    [ObservableProperty]
    public partial WallpaperFillMode Mode { get; set; } = WallpaperFillMode.Linear;
    partial void OnModeChanged(WallpaperFillMode value) => RaiseChanged();

    /// <summary>The solid colour, or the first colour of a gradient.</summary>
    [ObservableProperty]
    public partial Color StartColor { get; set; } = Color.FromRgb(0x1E, 0x3A, 0x8A);
    partial void OnStartColorChanged(Color value) => RaiseChanged();

    [ObservableProperty]
    public partial Color EndColor { get; set; } = Color.FromRgb(0x0B, 0x0F, 0x1A);
    partial void OnEndColorChanged(Color value) => RaiseChanged();

    /// <summary>Adds <see cref="MidColor"/> half way between the start and end colour.</summary>
    [ObservableProperty]
    public partial bool UseMidColor { get; set; }
    partial void OnUseMidColorChanged(bool value) => RaiseChanged();

    [ObservableProperty]
    public partial Color MidColor { get; set; } = Color.FromRgb(0x6D, 0x28, 0xD9);
    partial void OnMidColorChanged(Color value) => RaiseChanged();

    /// <summary>Linear direction in degrees: 0° = left→right, 90° = top→bottom (as for symbol layers).</summary>
    [ObservableProperty]
    public partial double Angle { get; set; } = 90.0;
    partial void OnAngleChanged(double value) => RaiseChanged();

    /// <summary>Radial centre, horizontally, in percent of the surface width.</summary>
    [ObservableProperty]
    public partial double CenterX { get; set; } = 50.0;
    partial void OnCenterXChanged(double value) => RaiseChanged();

    /// <summary>Radial centre, vertically, in percent of the surface height.</summary>
    [ObservableProperty]
    public partial double CenterY { get; set; } = 50.0;
    partial void OnCenterYChanged(double value) => RaiseChanged();

    /// <summary>Radial extent in percent of half the surface diagonal.</summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 100.0;
    partial void OnRadiusChanged(double value) => RaiseChanged();

    /// <summary>Strength (0..1) of the darkening towards the edges.</summary>
    [ObservableProperty]
    public partial double Vignette { get; set; }
    partial void OnVignetteChanged(double value) => RaiseChanged();

    /// <summary>Strength (0..1) of a fixed fine grain that breaks up colour banding.</summary>
    [ObservableProperty]
    public partial double Noise { get; set; }
    partial void OnNoiseChanged(double value) => RaiseChanged();

    /// <summary>Raised whenever a property that affects the painted background changes.</summary>
    public event EventHandler Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public WallpaperFill Clone()
    {
        WallpaperFill copy = new();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(WallpaperFill other)
    {
        if (other == null) return;
        Enabled = other.Enabled;
        Mode = other.Mode;
        StartColor = other.StartColor;
        EndColor = other.EndColor;
        UseMidColor = other.UseMidColor;
        MidColor = other.MidColor;
        Angle = other.Angle;
        CenterX = other.CenterX;
        CenterY = other.CenterY;
        Radius = other.Radius;
        Vignette = other.Vignette;
        Noise = other.Noise;
    }

    /// <summary>Resets to the disabled default.</summary>
    public void Clear() => CopyFrom(new WallpaperFill());
}
