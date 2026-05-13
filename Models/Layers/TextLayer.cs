using Avalonia.Media;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders text. Inherits position/scale from <see cref="LayerBase"/>;
/// when <see cref="Centered"/> is true, position is interpreted as an offset from
/// the button center, otherwise as the upper-left corner.
/// </summary>
public class TextLayer : LayerBase
{
    public const string Kind = "text";

    private string _text = string.Empty;
    private int _textSize = 16;
    private Color _textColor = Colors.White;
    private bool _bold;
    private bool _italic;
    private bool _outlined;
    private Color _outlineColor = Colors.Black;
    private bool _centered = true;
    private int _boxWidth;
    private int _boxHeight;

    /// <summary>
    /// Width of the text-layout box in device pixels. The renderer wraps text
    /// at this width and (when <see cref="Centered"/> is true) centers within it.
    /// <c>0</c> means "fall back to the device size" — covers configs persisted
    /// before this property existed.
    /// </summary>
    public int BoxWidth
    {
        get => _boxWidth;
        set => SetField(ref _boxWidth, value);
    }

    public int BoxHeight
    {
        get => _boxHeight;
        set => SetField(ref _boxHeight, value);
    }

    [JsonIgnore]
    public int EffectiveBoxWidth => _boxWidth > 0 ? _boxWidth : 90;

    [JsonIgnore]
    public int EffectiveBoxHeight => _boxHeight > 0 ? _boxHeight : 90;

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public int TextSize
    {
        get => _textSize;
        set => SetField(ref _textSize, value);
    }

    public Color TextColor
    {
        get => _textColor;
        set => SetField(ref _textColor, value);
    }

    public bool Bold
    {
        get => _bold;
        set => SetField(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => SetField(ref _italic, value);
    }

    public bool Outlined
    {
        get => _outlined;
        set => SetField(ref _outlined, value);
    }

    public Color OutlineColor
    {
        get => _outlineColor;
        set => SetField(ref _outlineColor, value);
    }

    public bool Centered
    {
        get => _centered;
        set => SetField(ref _centered, value);
    }

    public override string LayerKind => Kind;
}
