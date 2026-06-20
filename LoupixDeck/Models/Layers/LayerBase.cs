using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// Base class for all touch-button layers (image, text, symbol, …).
/// Property changes fire <see cref="INotifyPropertyChanged"/> so the owning
/// <see cref="TouchButton"/> can re-render. Position/Scale are expressed in
/// 90×90 device-pixel space; the editor canvas applies its own zoom factor.
/// </summary>
public abstract class LayerBase : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _visible = true;
    private int _positionX;
    private int _positionY;
    private double _scale = 1.0;
    private double _scaleY;
    private double _rotation;
    private string _ownerKey;
    private string _commandName;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// Identifies the display command (core or plugin) that owns this layer's content.
    /// <c>null</c> for a normal user-created layer; non-null marks the layer as
    /// <b>command-owned</b>: its content is driven by the bound command, it cannot be deleted
    /// manually in the editor, and it is swept/demoted when the command unbinds (see the
    /// dynamic-text manager). The value is the canonical <c>name(p1,p2,…)</c> form produced by
    /// <see cref="PluginLayerKey.For"/>. Persisted; absent in older configs (defaults null).
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string OwnerKey
    {
        get => _ownerKey;
        set
        {
            if (SetField(ref _ownerKey, value))
                OnPropertyChanged(nameof(IsCommandOwned));
        }
    }

    /// <summary>
    /// Human-readable name of the owning display command (e.g. for the editor badge/info
    /// card). Set alongside <see cref="OwnerKey"/> on command-owned layers; <c>null</c>
    /// otherwise. Persisted; absent in older configs.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string CommandName
    {
        get => _commandName;
        set => SetField(ref _commandName, value);
    }

    /// <summary>True when this layer's content is owned by a display command (core or plugin).</summary>
    [JsonIgnore]
    public bool IsCommandOwned => !string.IsNullOrEmpty(_ownerKey);

    /// <summary>
    /// True when the layer was created by its owning command (vs adopted from a pre-existing
    /// user layer). On orphan, a created layer is removed entirely, while an adopted one is only
    /// demoted back to a normal user layer so the user's styling is never destroyed. Persisted so
    /// the distinction survives a save; omitted when false. Only meaningful with <see cref="OwnerKey"/>.
    /// </summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool OwnerCreated { get; set; }

    public bool Visible
    {
        get => _visible;
        set => SetField(ref _visible, value);
    }

    public int PositionX
    {
        get => _positionX;
        set => SetField(ref _positionX, value);
    }

    public int PositionY
    {
        get => _positionY;
        set => SetField(ref _positionY, value);
    }

    public double Scale
    {
        get => _scale;
        set
        {
            if (SetField(ref _scale, value))
            {
                OnPropertyChanged(nameof(EffectiveScaleX));
                OnPropertyChanged(nameof(EffectiveScaleY));
                OnDisplaySizeChanged();
            }
        }
    }

    /// <summary>
    /// Optional Y-axis multiplier. <c>0</c> means "follow <see cref="Scale"/>" so
    /// existing layers keep uniform behavior; anything &gt; 0 enables anisotropic
    /// resize (e.g. Shift-drag breaks aspect lock).
    /// </summary>
    public double ScaleY
    {
        get => _scaleY;
        set
        {
            if (SetField(ref _scaleY, value))
            {
                OnPropertyChanged(nameof(EffectiveScaleY));
                OnDisplaySizeChanged();
            }
        }
    }

    [JsonIgnore]
    public double EffectiveScaleX => _scale <= 0 ? 1.0 : _scale;

    [JsonIgnore]
    public double EffectiveScaleY => _scaleY > 0 ? _scaleY : EffectiveScaleX;

    /// <summary>
    /// Displayed width of the layer in 90×90 device-pixel space. Bridges the
    /// editor's size fields to the underlying <see cref="Scale"/> multiplier.
    /// Base implementation is inert; concrete layers that have a resolvable
    /// size (image, symbol) override it.
    /// </summary>
    [JsonIgnore]
    public virtual double DisplayWidth
    {
        get => 0;
        set { }
    }

    /// <summary>
    /// Displayed height of the layer in 90×90 device-pixel space. See
    /// <see cref="DisplayWidth"/>.
    /// </summary>
    [JsonIgnore]
    public virtual double DisplayHeight
    {
        get => 0;
        set { }
    }

    /// <summary>
    /// Raises change notifications for <see cref="DisplayWidth"/> /
    /// <see cref="DisplayHeight"/> so the editor's size fields track changes
    /// made via scale, crop or drag-resize.
    /// </summary>
    protected void OnDisplaySizeChanged()
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
    }

    public double Rotation
    {
        get => _rotation;
        set => SetField(ref _rotation, value);
    }

    [JsonIgnore]
    public abstract string LayerKind { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
