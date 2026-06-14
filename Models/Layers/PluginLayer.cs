using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A plugin-owned layer whose pixels are rendered by a plugin and pushed to the host at
/// runtime (via an <c>IDisplayImageCommand</c>), rather than loaded from an asset like
/// <see cref="ImageLayer"/>. The host only blits <see cref="RenderedBitmap"/> (with the
/// same fit/scale/position math as an image layer).
///
/// This layer kind can only be created by the dynamic-text manager (never via the editor's
/// add buttons). It is plugin-managed (see <see cref="LayerBase.OwnerKey"/>): the user may
/// move/scale/rotate/hide/reorder it, but not edit its content or delete it directly.
/// </summary>
public class PluginLayer : LayerBase
{
    public const string Kind = "plugin";

    /// <summary>Device-pixel frame size the image is fitted into (90×90 button).</summary>
    private const double DeviceBaseSize = 90.0;

    private SKBitmap _renderedBitmap;

    // A bitmap just replaced as RenderedBitmap may still be read by an in-flight render
    // (device push / editor preview) that captured the reference around the swap. Disposing
    // it immediately would risk a use-after-free, so retire it under the render gate and
    // dispose only a few swaps later — mirrors TouchButton.RenderedImage.
    private readonly Queue<SKBitmap> _retiredBitmaps = new();
    private const int RetainedGenerations = 3;

    /// <summary>
    /// The plugin-rendered bitmap. Runtime-only (never serialized): re-pushed on the next
    /// poll after a config load. Old bitmaps are retired/disposed under the Skia render gate.
    /// </summary>
    [JsonIgnore]
    public SKBitmap RenderedBitmap
    {
        get => _renderedBitmap;
        set
        {
            bool changed;
            lock (SkiaRenderGate.Sync)
            {
                if (ReferenceEquals(_renderedBitmap, value))
                    return;

                var old = _renderedBitmap;
                _renderedBitmap = value;
                if (old != null)
                    _retiredBitmaps.Enqueue(old);
                while (_retiredBitmaps.Count > RetainedGenerations)
                    _retiredBitmaps.Dequeue().Dispose();
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged();
                OnDisplaySizeChanged();
            }
        }
    }

    /// <summary>
    /// Disposes the current and all retired bitmaps under the render gate. Called by the
    /// dynamic-text manager when this layer is swept (its owning command unbound).
    /// </summary>
    public void DisposeBitmaps()
    {
        lock (SkiaRenderGate.Sync)
        {
            _renderedBitmap?.Dispose();
            _renderedBitmap = null;
            while (_retiredBitmaps.Count > 0)
                _retiredBitmaps.Dequeue().Dispose();
        }
    }

    private (double Width, double Height)? GetSourceDimensions()
    {
        if (_renderedBitmap is { Width: > 0, Height: > 0 })
            return (_renderedBitmap.Width, _renderedBitmap.Height);

        return null;
    }

    [JsonIgnore]
    public override double DisplayWidth
    {
        get
        {
            if (GetSourceDimensions() is not { } src) return 0;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            return src.Width * fit * EffectiveScaleX;
        }
        set
        {
            if (value <= 0 || GetSourceDimensions() is not { } src) return;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            var baseW = src.Width * fit;
            if (baseW <= 0) return;
            // Lock the current height first so editing width keeps height fixed.
            if (ScaleY <= 0) ScaleY = EffectiveScaleY;
            Scale = value / baseW;
        }
    }

    [JsonIgnore]
    public override double DisplayHeight
    {
        get
        {
            if (GetSourceDimensions() is not { } src) return 0;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            return src.Height * fit * EffectiveScaleY;
        }
        set
        {
            if (value <= 0 || GetSourceDimensions() is not { } src) return;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            var baseH = src.Height * fit;
            if (baseH <= 0) return;
            ScaleY = value / baseH;
        }
    }

    public override string LayerKind => Kind;
}
