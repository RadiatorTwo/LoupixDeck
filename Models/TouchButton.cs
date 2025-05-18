using Avalonia.Media;
using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public class TouchButton(int index) : LoupedeckButton
{
    public int Index { get; } = index;

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text) return;
            _text = value;
            //OnPropertyChanged(nameof(Text));
            Refresh();
        }
    }

    private bool _textCentered = true;

    public bool TextCentered
    {
        get => _textCentered;
        set
        {
            if (value == _textCentered) return;
            _textCentered = value;
            Refresh();
            OnPropertyChanged(nameof(TextCentered));
        }
    }

    private int _textSize = 16;

    public int TextSize
    {
        get => _textSize;
        set
        {
            if (value == _textSize) return;
            _textSize = value;
            Refresh();
        }
    }

    private int _textPositionX;

    public int TextPositionX
    {
        get => _textPositionX;
        set
        {
            if (_textPositionY == value) return;
            _textPositionX = value;
            Refresh();
        }
    }

    private int _textPositionY;

    public int TextPositionY
    {
        get => _textPositionY;
        set
        {
            if (_textPositionY == value) return;
            _textPositionY = value;
            Refresh();
        }
    }

    private Color _textColor = Colors.White;

    public Color TextColor
    {
        get => _textColor;
        set
        {
            if (Equals(value, _textColor)) return;
            _textColor = value;
            Refresh();
        }
    }

    private bool _bold;

    public bool Bold
    {
        get => _bold;
        set
        {
            if (value == _bold) return;
            _bold = value;
            Refresh();
        }
    }

    private bool _italic;

    public bool Italic
    {
        get => _italic;
        set
        {
            if (value == _italic) return;
            _italic = value;
            Refresh();
        }
    }

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

    private bool _outlined;

    public bool Outlined
    {
        get => _outlined;
        set
        {
            if (value == _outlined) return;
            _outlined = value;
            Refresh();
            OnPropertyChanged(nameof(Outlined));
        }
    }

    private Color _outlineColor = Colors.Black;

    public Color OutlineColor
    {
        get => _outlineColor;
        set
        {
            if (Equals(value, _outlineColor)) return;
            _outlineColor = value;
            Refresh();
        }
    }

    private SKBitmap _image;

    [JsonConverter(typeof(SKBitmapBase64Converter))]
    public SKBitmap Image
    {
        get => _image;
        set
        {
            if (Equals(value, _image)) return;
            _image = value;
            Refresh();
        }
    }

    private int _imagePositionX;

    public int ImagePositionX
    {
        get => _imagePositionX;
        set
        {
            if (_imagePositionX == value) return;
            _imagePositionX = value;
            Refresh();
        }
    }

    private int _imagePositionY;

    public int ImagePositionY
    {
        get => _imagePositionY;
        set
        {
            if (_imagePositionY == value) return;
            _imagePositionY = value;
            Refresh();
        }
    }

    private int _imageScale = 100;

    public int ImageScale
    {
        get => _imageScale;
        set
        {
            if (_imageScale == value) return;
            _imageScale = value;
            Refresh();
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
}