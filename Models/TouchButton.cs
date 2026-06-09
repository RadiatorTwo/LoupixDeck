using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public class TouchButton : LoupedeckButton
{
    public TouchButton(int index)
    {
        Index = index;
        Layers = new System.Collections.ObjectModel.ObservableCollection<LayerBase>();
        AttachLayerHandlers(Layers);
    }

    /// <summary>Parameterless ctor for the JSON deserializer.</summary>
    [JsonConstructor]
    private TouchButton() : this(0)
    {
    }

    public int Index { get; set; }

    private Color _backColor = Colors.Black;

    public Color BackColor
    {
        get => _backColor;
        set
        {
            if (Equals(value, _backColor)) return;
            _backColor = value;
            Refresh();
        }
    }

    private bool _vibrationEnabled;

    public bool VibrationEnabled
    {
        get => _vibrationEnabled;
        set
        {
            if (value == _vibrationEnabled) return;
            _vibrationEnabled = value;
            OnPropertyChanged(nameof(VibrationEnabled));
        }
    }

    private byte _vibrationPattern;

    public byte VibrationPattern
    {
        get => _vibrationPattern == 0
            ? LoupedeckDevice.Constants.VibrationPattern.ShortLower
            : _vibrationPattern;
        set
        {
            if (value == _vibrationPattern) return;
            _vibrationPattern = value;
            OnPropertyChanged(nameof(VibrationPattern));
        }
    }

    private System.Collections.ObjectModel.ObservableCollection<LayerBase> _layers;

    public System.Collections.ObjectModel.ObservableCollection<LayerBase> Layers
    {
        get => _layers;
        set
        {
            if (ReferenceEquals(_layers, value)) return;
            DetachLayerHandlers(_layers);
            _layers = value ?? new System.Collections.ObjectModel.ObservableCollection<LayerBase>();
            AttachLayerHandlers(_layers);
            Refresh();
            OnPropertyChanged(nameof(Layers));
        }
    }

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

    private void AttachLayerHandlers(System.Collections.ObjectModel.ObservableCollection<LayerBase> layers)
    {
        if (layers == null) return;
        layers.CollectionChanged += Layers_CollectionChanged;
        foreach (var layer in layers)
        {
            if (layer != null) layer.PropertyChanged += Layer_PropertyChanged;
        }
    }

    private void DetachLayerHandlers(System.Collections.ObjectModel.ObservableCollection<LayerBase> layers)
    {
        if (layers == null) return;
        layers.CollectionChanged -= Layers_CollectionChanged;
        foreach (var layer in layers)
        {
            if (layer != null) layer.PropertyChanged -= Layer_PropertyChanged;
        }
    }

    private void Layers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (LayerBase l in e.OldItems)
                if (l != null) l.PropertyChanged -= Layer_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (LayerBase l in e.NewItems)
                if (l != null) l.PropertyChanged += Layer_PropertyChanged;
        }

        Refresh();
    }

    private void Layer_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        Refresh();
    }

    /// <summary>
    /// Returns the first <see cref="TextLayer"/> on this button, creating one
    /// (and appending it as the top-most layer) if none exists. Intended for
    /// dynamic-text providers that need a stable text target.
    /// </summary>
    public TextLayer GetOrCreatePrimaryTextLayer()
    {
        foreach (var layer in Layers)
        {
            if (layer is TextLayer text) return text;
        }

        var created = new TextLayer { Name = "Text", BoxWidth = 90, BoxHeight = 90 };
        Layers.Add(created);
        return created;
    }

    /// <summary>
    /// Re-attaches PropertyChanged handlers to all layers — call after JSON
    /// deserialization since the ObservableCollection setter wires up the
    /// collection events but the individual layers were constructed by the
    /// JSON converter, bypassing AttachLayerHandlers.
    /// </summary>
    public void RewireLayerHandlers()
    {
        if (_layers == null) return;
        foreach (var layer in _layers)
        {
            if (layer == null) continue;
            layer.PropertyChanged -= Layer_PropertyChanged;
            layer.PropertyChanged += Layer_PropertyChanged;
        }
    }
}
