using SkiaSharp;

namespace LoupixDeck.Commands.Base;

/// <summary>
/// A core command that draws its own content instead of returning text. The host polls it every
/// <see cref="UpdateInterval"/> and hands it the Skia canvas of the target region, so it can draw
/// with sub-pixel precision (the plugin-facing canvas only takes whole pixels).
/// </summary>
public interface IDynamicImageProvider : IExecutableCommand
{
    TimeSpan UpdateInterval { get; }

    /// <summary>
    /// Draws onto <paramref name="canvas"/>, a <paramref name="width"/> x <paramref name="height"/>
    /// region whose origin is (0, 0). Returns false to leave the target unchanged.
    /// </summary>
    bool Render(string[] parameters, SKCanvas canvas, int width, int height);
}
