using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

public partial class SimpleButton : LoupedeckButton
{
    public Constants.ButtonType Id { get; set; }

    public Color ButtonColor
    {
        get;
        set
        {
            if (value.Equals(field)) return;
            field = value;
            //OnPropertyChanged(nameof(TextColor));
            Refresh();
        }
    }

    [JsonIgnore]
    [ObservableProperty]
    public partial Bitmap RenderedImage { get; set; }
}