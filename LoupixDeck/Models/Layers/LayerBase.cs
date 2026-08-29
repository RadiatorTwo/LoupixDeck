using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// Base class for all touch-button layers (image, text, symbol, …).
/// Property changes fire <see cref="INotifyPropertyChanged"/> so the owning
/// <see cref="TouchButton"/> can re-render. Position/Scale are expressed in the
/// device's own key-pixel space (see <see cref="DeviceBaseWidth"/>); the editor
/// canvas applies its own zoom factor.
/// </summary>
[ObservableObject]
public abstract partial class LayerBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// Identifies the display command (core or plugin) that owns this layer's content.
    /// <c>null</c> for a normal user-created layer; non-null marks the layer as
    /// <b>command-owned</b>: its content is driven by the bound command, it cannot be deleted
    /// manually in the editor, and it is swept/demoted when the command unbinds (see the
    /// dynamic-text manager). The value is the canonical <c>name(p1,p2,…)</c> form produced by
    /// <see cref="PluginLayerKey.For"/>. Persisted; absent in older configs (defaults null).
    /// </summary>
    [field: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommandOwned))]
    public partial string OwnerKey { get; set; }

    /// <summary>
    /// Human-readable name of the owning display command (e.g. for the editor badge/info
    /// card). Set alongside <see cref="OwnerKey"/> on command-owned layers; <c>null</c>
    /// otherwise. Persisted; absent in older configs.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [ObservableProperty]
    public partial string CommandName { get; set; }

    /// <summary>True when this layer's content is owned by a display command (core or plugin).</summary>
    [JsonIgnore]
    public bool IsCommandOwned => !string.IsNullOrEmpty(OwnerKey);

    /// <summary>
    /// True when the layer was created by its owning command (vs adopted from a pre-existing
    /// user layer). On orphan, a created layer is removed entirely, while an adopted one is only
    /// demoted back to a normal user layer so the user's styling is never destroyed. Persisted so
    /// the distinction survives a save; omitted when false. Only meaningful with <see cref="OwnerKey"/>.
    /// </summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool OwnerCreated { get; set; }

    [ObservableProperty]
    public partial bool Visible { get; set; } = true;

    [ObservableProperty]
    public partial int PositionX { get; set; }

    [ObservableProperty]
    public partial int PositionY { get; set; }

    public double Scale
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(EffectiveScaleX));
                OnPropertyChanged(nameof(EffectiveScaleY));
                OnDisplaySizeChanged();
            }
        }
    } = 1.0;

    /// <summary>
    /// Optional Y-axis multiplier. <c>0</c> means "follow <see cref="Scale"/>" so
    /// existing layers keep uniform behavior; anything &gt; 0 enables anisotropic
    /// resize (e.g. Shift-drag breaks aspect lock).
    /// </summary>
    public double ScaleY
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(EffectiveScaleY));
                OnDisplaySizeChanged();
            }
        }
    }

    [JsonIgnore]
    public double EffectiveScaleX => Scale <= 0 ? 1.0 : Scale;

    [JsonIgnore]
    public double EffectiveScaleY => ScaleY > 0 ? ScaleY : EffectiveScaleX;

    /// <summary>
    /// Size of the surface this layer is drawn onto, in device pixels — one key on the
    /// touch grid, or the side strip when editing a strip canvas. Runtime-only and never
    /// persisted: the renderer computes from the width/height it is handed, and only the
    /// editor's size fields read the projections below. Defaults to the Loupedeck family's
    /// 90px key so every non-editor path is unchanged; the layer editor assigns the real
    /// size of the device being edited.
    /// </summary>
    [JsonIgnore]
    public int DeviceBaseWidth
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnDisplaySizeChanged();
        }
    } = 90;

    /// <inheritdoc cref="DeviceBaseWidth"/>
    [JsonIgnore]
    public int DeviceBaseHeight
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnDisplaySizeChanged();
        }
    } = 90;

    /// <summary>
    /// Reference edge for layers that scale uniformly (symbol glyphs, fitted images):
    /// the short side of the surface, which is what the renderer's Math.Min(width, height)
    /// resolves to.
    /// </summary>
    [JsonIgnore]
    protected double DeviceBaseSize => Math.Min(DeviceBaseWidth, DeviceBaseHeight);

    /// <summary>
    /// Displayed width of the layer in device-pixel space. Bridges the
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
    /// Displayed height of the layer in device-pixel space. See
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

    [ObservableProperty]
    public partial double Rotation { get; set; }

    [JsonIgnore]
    public abstract string LayerKind { get; }
}
