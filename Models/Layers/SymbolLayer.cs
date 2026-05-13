using Avalonia.Media;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// Placeholder for the upcoming symbol-library feature. Until the picker exists
/// the renderer draws a labeled placeholder box.
/// </summary>
public class SymbolLayer : LayerBase
{
    public const string Kind = "symbol";

    private string _symbolId = string.Empty;
    private Color _tint = Colors.White;

    public string SymbolId
    {
        get => _symbolId;
        set => SetField(ref _symbolId, value);
    }

    public Color Tint
    {
        get => _tint;
        set => SetField(ref _tint, value);
    }

    public override string LayerKind => Kind;
}
