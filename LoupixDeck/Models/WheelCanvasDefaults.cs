using Avalonia.Media;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.Models;

/// <summary>
/// Builds the starting layer canvas for the Loupedeck CT's centre wheel: the automatic layout
/// (<c>BitmapHelper.RenderWheelScreen</c>) expressed as editable layers, so "Edit appearance"
/// opens on what the wheel already shows. Positions and boxes are derived from that renderer's
/// proportions at the given screen size (the 0.62 text square, the 40/60 split between label
/// and value), so at the default 240 px they match pixel for pixel when both are present.
/// </summary>
public static class WheelCanvasDefaults
{
    public const string IndicatorLayerName = "Indicator";
    public const string LabelLayerName = "Label";
    public const string ValueLayerName = "Value";

    /// <summary>A new canvas for a <paramref name="size"/>-pixel square wheel screen.</summary>
    public static TouchButton Create(int size)
    {
        var canvas = new TouchButton(RotaryButton.WheelCanvasIndex);
        Populate(canvas, size);
        return canvas;
    }

    /// <summary>
    /// Replaces <paramref name="canvas"/>'s background and layers with the default look.
    /// </summary>
    public static void Populate(TouchButton canvas, int size)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (size <= 0) size = 240;

        canvas.BackgroundEnabled = true;
        canvas.BackColor = Colors.Black;
        canvas.Layers.Clear();

        float textBox = size * 0.62f;
        float textLeft = (size - textBox) / 2f;
        float half = textBox / 2f;
        int boxW = (int)Math.Round(textBox);
        int labelH = (int)Math.Round(half * 0.8f);
        int valueH = (int)Math.Round(half * 1.2f);
        // Centred boxes are positioned by their offset from the screen centre.
        int labelY = (int)Math.Round(textLeft + labelH / 2f - size / 2f);
        int valueY = (int)Math.Round(textLeft + half * 0.8f + valueH / 2f - size / 2f);

        canvas.Layers.Add(new DialIndicatorLayer { Name = IndicatorLayerName });
        canvas.Layers.Add(new TextLayer
        {
            Name = LabelLayerName,
            TextSource = TextSource.DialLabel,
            TextSize = 22,
            Centered = true,
            BoxWidth = boxW,
            BoxHeight = labelH,
            PositionY = labelY
        });
        canvas.Layers.Add(new TextLayer
        {
            Name = ValueLayerName,
            TextSource = TextSource.DialValue,
            TextSize = 40,
            Bold = true,
            Centered = true,
            BoxWidth = boxW,
            BoxHeight = valueH,
            PositionY = valueY
        });
    }
}
