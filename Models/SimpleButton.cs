using Avalonia.Media;
using Avalonia.Media.Imaging;
using LoupixDeck.LoupedeckDevice;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

public class SimpleButton : LoupedeckButton
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
    
    private Bitmap _renderedImage;
    [JsonIgnore]
    public Bitmap RenderedImage
    {
        get => _renderedImage;
        set
        {
            if (_renderedImage == value) return;
            _renderedImage = value;
            OnPropertyChanged(nameof(RenderedImage));
        }
    }
}