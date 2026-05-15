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

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

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
