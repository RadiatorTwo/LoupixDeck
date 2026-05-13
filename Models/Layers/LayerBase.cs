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
        set => SetField(ref _scale, value);
    }

    /// <summary>
    /// Optional Y-axis multiplier. <c>0</c> means "follow <see cref="Scale"/>" so
    /// existing layers keep uniform behavior; anything &gt; 0 enables anisotropic
    /// resize (e.g. Shift-drag breaks aspect lock).
    /// </summary>
    public double ScaleY
    {
        get => _scaleY;
        set => SetField(ref _scaleY, value);
    }

    [JsonIgnore]
    public double EffectiveScaleX => _scale <= 0 ? 1.0 : _scale;

    [JsonIgnore]
    public double EffectiveScaleY => _scaleY > 0 ? _scaleY : EffectiveScaleX;

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
