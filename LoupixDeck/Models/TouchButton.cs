using System.Collections.ObjectModel;
using Avalonia.Media;
using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

/// <summary>
/// A touch/LCD key. Holds one or more named <see cref="ButtonState"/>s; the button's rendered
/// appearance (background + layers) and its press command/haptic are delegated to the active
/// state. A button with a single state behaves exactly like a pre-stateful button — the v6→v7
/// migration wraps every old button into one "Default" state.
/// </summary>
public class TouchButton : StatefulButton
{
    public TouchButton(int index)
    {
        Index = index;
    }

    /// <summary>Parameterless ctor for the JSON deserializer.</summary>
    [JsonConstructor]
    private TouchButton() : this(0)
    {
    }

    public int Index { get; set; }

    protected override void RaiseActiveStateProjections()
    {
        OnPropertyChanged(nameof(Layers));
        OnPropertyChanged(nameof(BackColor));
        OnPropertyChanged(nameof(VibrationEnabled));
        OnPropertyChanged(nameof(VibrationPattern));
    }

    // ---- Appearance / haptic projected onto the active state --------------

    [JsonIgnore]
    public Color BackColor
    {
        get => ActiveState?.BackColor ?? Colors.Black;
        set
        {
            if (ActiveState == null || Equals(ActiveState.BackColor, value)) return;
            ActiveState.BackColor = value;
            OnPropertyChanged(nameof(BackColor));
        }
    }

    [JsonIgnore]
    public bool VibrationEnabled
    {
        get => ActiveState?.VibrationEnabled ?? false;
        set
        {
            if (ActiveState == null || ActiveState.VibrationEnabled == value) return;
            ActiveState.VibrationEnabled = value;
            OnPropertyChanged(nameof(VibrationEnabled));
        }
    }

    [JsonIgnore]
    public byte VibrationPattern
    {
        get => ActiveState?.VibrationPattern ?? LoupedeckDevice.Constants.VibrationPattern.ShortLower;
        set
        {
            if (ActiveState == null || ActiveState.VibrationPattern == value) return;
            ActiveState.VibrationPattern = value;
            OnPropertyChanged(nameof(VibrationPattern));
        }
    }

    [JsonIgnore]
    public ObservableCollection<LayerBase> Layers
    {
        get => ActiveState?.Layers;
        set
        {
            if (ActiveState == null) return;
            ActiveState.Layers = value;
            OnPropertyChanged(nameof(Layers));
        }
    }

    // ---- Rendered bitmap (unchanged) --------------------------------------

    private SKBitmap _renderedImage;

    // A bitmap just replaced as RenderedImage may still be read by an in-flight reader
    // that captured the reference around the swap: the UI preview-converter (not gated,
    // copies the pixels) or the device push (reads RenderedImage, then converts across
    // an await in DrawKey -> DrawCanvas). Disposing it immediately would risk a
    // use-after-free, so retire it and dispose only a few swaps later — by then every
    // such reader has long finished. This bounds the native pixel memory instead of
    // leaving it to the GC finalizer (crash-analysis measure 4). Three generations of
    // headroom comfortably covers the widest reader window (the awaited device push).
    private readonly Queue<SKBitmap> _retiredImages = new();
    private const int RetainedRenderedGenerations = 3;

    [JsonIgnore]
    public SKBitmap RenderedImage
    {
        get => _renderedImage;
        set
        {
            if (ReferenceEquals(value, _renderedImage)) return;

            // Swap + retire under the render gate so the deferred native Dispose()
            // never overlaps active Skia work and concurrent setters stay consistent.
            // OnPropertyChanged is raised outside the lock (it may marshal to the UI).
            lock (SkiaRenderGate.Sync)
            {
                var old = _renderedImage;
                _renderedImage = value;
                if (old != null)
                    _retiredImages.Enqueue(old);
                while (_retiredImages.Count > RetainedRenderedGenerations)
                    _retiredImages.Dequeue().Dispose();
            }

            OnPropertyChanged(nameof(RenderedImage));
        }
    }

    // ---- Command-owned layer helpers (forward to the active state) --------

    /// <summary>
    /// Returns the plugin-managed <see cref="PluginLayer"/> bound to <paramref name="ownerKey"/>
    /// on the active state, creating one (appended on top) if none exists.
    /// </summary>
    public PluginLayer GetOrCreatePluginLayer(string ownerKey, string commandName)
        => ActiveState?.GetOrCreatePluginLayer(ownerKey, commandName);

    /// <summary>
    /// Returns the command-owned <see cref="TextLayer"/> bound to <paramref name="ownerKey"/>
    /// on the active state, adopting or creating it as needed.
    /// </summary>
    public TextLayer GetOrAdoptOwnedTextLayer(string ownerKey, string commandName)
        => ActiveState?.GetOrAdoptOwnedTextLayer(ownerKey, commandName);

    /// <summary>
    /// Re-attaches per-layer PropertyChanged handlers on every state and re-subscribes the active
    /// state after JSON deserialization (the layers/states are built by the JSON converters and
    /// bypass the collection setters). Also re-mirrors the active command.
    /// </summary>
    public void RewireLayerHandlers()
    {
        if (States != null)
        {
            foreach (var state in States)
                state?.RewireLayerHandlers();
        }

        NormalizeActiveStateAfterLoad();
    }
}
