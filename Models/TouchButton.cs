using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using LoupixDeck.Models.Layers;
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

    /// <summary>
    /// When the user enables EnableWhenOff we auto-enable vibration too: with
    /// the screen black during OFF, the haptic pulse is the only feedback the
    /// user gets that the touch was registered. Unchecking EnableWhenOff
    /// leaves VibrationEnabled alone — the two are independent from that
    /// direction.
    /// </summary>
    public override bool EnableWhenOff
    {
        get => base.EnableWhenOff;
        set
        {
            var was = base.EnableWhenOff;
            base.EnableWhenOff = value;
            if (!was && value && !VibrationEnabled)
                VibrationEnabled = true;
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

    [JsonIgnore]
    public SKBitmap RenderedImage
    {
        get => _renderedImage;
        set
        {
            if (Equals(value, _renderedImage)) return;
            _renderedImage = value;
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
