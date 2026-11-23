namespace LoupixDeck.Models;

/// <summary>
/// A draw-time adjustment of a key's layers: every layer moved right by <paramref name="ShiftX"/>
/// pixels and no text box wider than <paramref name="MaxTextWidth"/> (0 for no limit). The default
/// draws the layers as they are. Keeps the keys under the Live S dial labels clear of them.
/// </summary>
public readonly record struct KeyInset(int ShiftX, int MaxTextWidth);
