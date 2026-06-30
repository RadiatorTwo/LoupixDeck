using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One named state of a stateful button. Owns all per-state data: the command sequence,
/// background color, haptic feedback, LED color, the ordered layer stack and the transition
/// applied after the command runs (Local mode). A button with a single state behaves exactly
/// like a pre-stateful button — the migration wraps every old button into one "Default" state.
/// </summary>
public partial class ButtonState : ObservableObject
{
    public ButtonState()
    {
        Id = Guid.NewGuid();
        _layers = new ObservableCollection<LayerBase>();
        AttachLayerHandlers(_layers);
        Transition = new StateTransition();
    }

    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "State";

    /// <summary>The &amp;&amp;-joined command sequence run when the button is pressed in this state.</summary>
    [ObservableProperty]
    public partial string Command { get; set; }

    /// <summary>LED color for LED-capable buttons (SimpleButton). Ignored by touch buttons.</summary>
    [ObservableProperty]
    public partial Color LedColor { get; set; } = Colors.Black;

    // A LED color change is an appearance change for LED-capable buttons (SimpleButton), so bubble
    // it through Changed exactly like Layers/BackColor do for touch buttons.
    partial void OnLedColorChanged(Color value) => RaiseChanged();

    [ObservableProperty]
    public partial bool VibrationEnabled { get; set; }

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

    [ObservableProperty]
    public partial StateTransition Transition { get; set; }

    /// <summary>Editor-only: 1-based position shown in the States list. Maintained by the editor.</summary>
    [JsonIgnore]
    [ObservableProperty]
    public partial int DisplayIndex { get; set; }

    /// <summary>Editor-only: whether this is the button's default state. Maintained by the editor.</summary>
    [JsonIgnore]
    [ObservableProperty]
    public partial bool IsDefault { get; set; }

    private Color _backColor = Colors.Black;

    public Color BackColor
    {
        get => _backColor;
        set
        {
            if (Equals(value, _backColor)) return;
            _backColor = value;
            OnPropertyChanged(nameof(BackColor));
            RaiseChanged();
        }
    }

    private ObservableCollection<LayerBase> _layers;

    public ObservableCollection<LayerBase> Layers
    {
        get => _layers;
        set
        {
            if (ReferenceEquals(_layers, value)) return;
            DetachLayerHandlers(_layers);
            _layers = value ?? new ObservableCollection<LayerBase>();
            AttachLayerHandlers(_layers);
            OnPropertyChanged(nameof(Layers));
            RaiseChanged();
        }
    }

    /// <summary>
    /// Raised whenever this state's rendered appearance changes (a layer was added/removed,
    /// a layer property changed, or the background color changed). The owning button subscribes
    /// to the active state's <see cref="Changed"/> to re-emit its own refresh.
    /// </summary>
    public event EventHandler Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void AttachLayerHandlers(ObservableCollection<LayerBase> layers)
    {
        if (layers == null) return;
        layers.CollectionChanged += Layers_CollectionChanged;
        foreach (var layer in layers)
        {
            if (layer != null) layer.PropertyChanged += Layer_PropertyChanged;
        }
    }

    private void DetachLayerHandlers(ObservableCollection<LayerBase> layers)
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

        RaiseChanged();
    }

    private void Layer_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        RaiseChanged();
    }

    /// <summary>
    /// Re-attaches per-layer PropertyChanged handlers after JSON deserialization, since the
    /// layers are constructed by <see cref="LayerJsonConverter"/> and bypass the collection setter.
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

    /// <summary>
    /// Returns the plugin-managed <see cref="PluginLayer"/> bound to <paramref name="ownerKey"/>,
    /// creating one (appended on top) if none exists.
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
    /// Returns the command-owned <see cref="TextLayer"/> bound to <paramref name="ownerKey"/>,
    /// adopting the existing primary text layer or creating one if the state has none.
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
}
