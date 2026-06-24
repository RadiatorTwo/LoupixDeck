#nullable enable
using SkiaSharp;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// JSON-friendly rectangle (SKRect contains private state that Newtonsoft cannot
/// always round-trip cleanly). Empty/zero-size means "no crop, use full source".
/// </summary>
public struct SerializableRect(float left, float top, float right, float bottom) : IEquatable<SerializableRect>
{
    public float Left { get; set; } = left;
    public float Top { get; set; } = top;
    public float Right { get; set; } = right;
    public float Bottom { get; set; } = bottom;

    public static SerializableRect Empty => new(0, 0, 0, 0);

    public readonly bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public readonly float Width => Right - Left;
    public readonly float Height => Bottom - Top;

    public readonly SKRect ToSKRect() => new(Left, Top, Right, Bottom);

    public static SerializableRect FromSKRect(SKRect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    public override readonly bool Equals(object? obj) => obj is SerializableRect r && Equals(r);
    public readonly bool Equals(SerializableRect other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    public static bool operator ==(SerializableRect left, SerializableRect right) => left.Equals(right);
    public static bool operator !=(SerializableRect left, SerializableRect right) => !left.Equals(right);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
}
