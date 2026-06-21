using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public partial class TouchButton : LoupedeckButton
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

    public Color BackColor
    {
        get;
        set
        {
            if (Equals(value, field)) return;
            field = value;
            Refresh();
        }
    } = Colors.Black;

    [ObservableProperty]
    public partial bool VibrationEnabled { get; set; }

    public byte VibrationPattern
    {
        get => field == 0
            ? LoupedeckDevice.Constants.VibrationPattern.ShortLower
            : field;
        set => SetProperty(ref field, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<LayerBase> Layers
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            DetachLayerHandlers(field);
            field = value ?? new System.Collections.ObjectModel.ObservableCollection<LayerBase>();
            AttachLayerHandlers(field);
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
            layer?.PropertyChanged += Layer_PropertyChanged;
        }
    }

    private void DetachLayerHandlers(System.Collections.ObjectModel.ObservableCollection<LayerBase> layers)
    {
        if (layers == null) return;
        layers.CollectionChanged -= Layers_CollectionChanged;
        foreach (var layer in layers)
        {
            layer?.PropertyChanged -= Layer_PropertyChanged;
        }
    }

    private void Layers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (LayerBase l in e.OldItems)
                l?.PropertyChanged -= Layer_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (LayerBase l in e.NewItems)
                l?.PropertyChanged += Layer_PropertyChanged;
        }

        Refresh();
    }

    private void Layer_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        Refresh();
    }

    /// <summary>
    /// Returns the plugin-managed <see cref="PluginLayer"/> bound to <paramref name="ownerKey"/>,
    /// creating one (appended on top) if none exists. Used by image display commands so each
    /// command's bitmap targets exactly its own layer instead of "the first matching one".
    /// </summary>
    public PluginLayer GetOrCreatePluginLayer(string ownerKey, string commandName)
    {
        foreach (var layer in Layers)
        {
            if (layer is PluginLayer plugin &&
                string.Equals(plugin.OwnerKey, ownerKey, StringComparison.Ordinal))
            {
                return plugin;
            }
        }

        var created = new PluginLayer
        {
            Name = string.IsNullOrEmpty(commandName) ? "Plugin" : commandName,
            OwnerKey = ownerKey,
            CommandName = commandName,
            OwnerCreated = true
        };
        Layers.Add(created);
        return created;
    }

    /// <summary>
    /// Returns the command-owned <see cref="TextLayer"/> bound to <paramref name="ownerKey"/>.
    /// If none is tagged yet, adopts the existing primary text layer (tagging it) so styling and
    /// position of buttons configured before owner-keying are preserved; only when the button has
    /// no text layer at all is a new one created. Used by every text display command (core or
    /// plugin) for unique targeting in place of the former first-matching-text-layer lookup.
    /// </summary>
    public TextLayer GetOrAdoptOwnedTextLayer(string ownerKey, string commandName)
    {
        TextLayer firstUntagged = null;
        foreach (var layer in Layers)
        {
            if (layer is not TextLayer text) continue;
            if (string.Equals(text.OwnerKey, ownerKey, StringComparison.Ordinal))
                return text;
            firstUntagged ??= string.IsNullOrEmpty(text.OwnerKey) ? text : null;
        }

        if (firstUntagged != null)
        {
            firstUntagged.OwnerKey = ownerKey;
            firstUntagged.CommandName = commandName;
            return firstUntagged;
        }

        var created = new TextLayer
        {
            Name = string.IsNullOrEmpty(commandName) ? "Text" : commandName,
            BoxWidth = 90,
            BoxHeight = 90,
            OwnerCreated = true,
            OwnerKey = ownerKey,
            CommandName = commandName
        };
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
        if (Layers == null) return;
        foreach (var layer in Layers)
        {
            if (layer == null) continue;
            layer.PropertyChanged -= Layer_PropertyChanged;
            layer.PropertyChanged += Layer_PropertyChanged;
        }
    }
}
