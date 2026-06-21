using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

public partial class SimpleButton : LoupedeckButton
{
    public Constants.ButtonType Id { get; set; }
    
    private Color _buttonColor;
    public Color ButtonColor
    {
        get => _buttonColor;
        set
        {
            if (value.Equals(_buttonColor)) return;
            _buttonColor = value;
            //OnPropertyChanged(nameof(TextColor));
            Refresh();
        }
    }

    [JsonIgnore]
    [ObservableProperty]
    public partial Bitmap RenderedImage { get; set; }
}