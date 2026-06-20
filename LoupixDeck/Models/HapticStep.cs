using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models.Converter;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class HapticStep
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEffectItem))]
    public partial byte Effect { get; set; } = Constants.VibrationPattern.SharpClick;

    [JsonIgnore]
    public VibrationPatternItem SelectedEffectItem
    {
        get => VibrationPatternCatalog.All.FirstOrDefault(p => p.Value == Effect) ?? VibrationPatternCatalog.All[0];
        set { if (value != null) Effect = value.Value; }
    }

    public byte Delay
    {
        get;
        set
        {
            var clamped = value < 0x04 ? (byte)0x04 : value;
            SetProperty(ref field, clamped);
        }
    } = 0x32;

    public byte Duration
    {
        get;
        set
        {
            var clamped = value < 0x02 ? (byte)0x02 : value;
            SetProperty(ref field, clamped);
        }
    } = 0x10;
}
