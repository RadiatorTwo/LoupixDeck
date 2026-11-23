namespace LoupixDeck.Models;

/// <summary>
/// A draw-time narrowing of a key's layer surface: the layers are laid out on the part of the key
/// right of <paramref name="Left"/> pixels, as if the key were that much narrower, and clipped to it.
/// The default draws them on the whole key. Keeps the keys under the Live S dial labels clear of them.
/// </summary>
public readonly record struct KeyInset(int Left);
