using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that draws a dial's adjustment indicator as an arc: a track for the whole range and
/// a fill up to the value the dial's adjustment command reports. The value itself is not part of
/// the layer; the renderer receives it through <see cref="Utils.DialRenderContext"/> at draw
/// time, so a layer without a live value (or a <c>NaN</c> one) draws nothing — the same rule the
/// automatic wheel layout follows. Only meaningful on surfaces that have an adjustment value,
/// today the Loupedeck CT's centre wheel.
/// <para>
/// Geometry: the arc is inscribed in a square box of <c>Min(width,height) · Scale</c> centred
/// at the layer position (the same box <see cref="SymbolLayer"/> uses), inset by half the
/// stroke plus <see cref="Inset"/> of the box so the round caps stay inside it.
/// <see cref="LayerBase.Rotation"/> is added to <see cref="StartAngle"/>. The defaults
/// reproduce the automatic layout's arc exactly at scale 1.
/// </para>
/// </summary>
public partial class DialIndicatorLayer : LayerBase
{
    public const string Kind = "indicator";

    /// <summary>Colour of the unfilled part of the arc.</summary>
    [ObservableProperty]
    public partial Color TrackColor { get; set; } = Color.FromRgb(0x30, 0x30, 0x30);

    /// <summary>Colour of the arc from the start angle up to the value.</summary>
    [ObservableProperty]
    public partial Color FillColor { get; set; } = Color.FromRgb(0xE0, 0xE0, 0xE0);

    /// <summary>Stroke width as a fraction of the layer box (0.045 = the automatic layout).</summary>
    [ObservableProperty]
    public partial double Thickness { get; set; } = 0.045;

    /// <summary>Extra gap between the box edge and the stroke, as a fraction of the box.</summary>
    [ObservableProperty]
    public partial double Inset { get; set; } = 0.06;

    /// <summary>Where the arc starts, in Skia degrees (0 = right, clockwise). 135 puts the gap at the bottom.</summary>
    [ObservableProperty]
    public partial double StartAngle { get; set; } = 135;

    /// <summary>How far the arc sweeps clockwise from the start, in degrees (360 = a full ring).</summary>
    [ObservableProperty]
    public partial double SweepAngle { get; set; } = 270;

    /// <summary>Rounded stroke ends; false gives flat ends.</summary>
    [ObservableProperty]
    public partial bool RoundCaps { get; set; } = true;

    [JsonIgnore]
    public override double DisplayWidth
    {
        get => DeviceBaseSize * EffectiveScaleX;
        set
        {
            if (value <= 0) return;
            if (ScaleY <= 0) ScaleY = EffectiveScaleY;
            Scale = value / DeviceBaseSize;
        }
    }

    [JsonIgnore]
    public override double DisplayHeight
    {
        get => DeviceBaseSize * EffectiveScaleY;
        set
        {
            if (value <= 0) return;
            ScaleY = value / DeviceBaseSize;
        }
    }

    public override string LayerKind => Kind;
}
