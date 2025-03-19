using Avalonia.Media;
using Avalonia.Media.Imaging;

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
            if (value.Equals(_textPositionX)) return;
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
            if (value.Equals(_textPositionY)) return;
            _textPositionY = value;
            Refresh();
        }
    }

    private Color _textColor = Colors.Black;

    public Color TextColor
    {
        get => _textColor;
        set
        {
            if (value.Equals(_textColor)) return;
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
            if (_backColor == value) return;
            _backColor = value;
            Refresh();
        }
    }

    private bool _outlined = true;

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

    private Color _outlineColor = Colors.White;

    public Color OutlineColor
    {
        get => _outlineColor;
        set
        {
            if (value.Equals(_outlineColor)) return;
            _outlineColor = value;
            Refresh();
        }
    }

    private Bitmap _image;

    public Bitmap Image
    {
        get => _image;
        set
        {
            if (_image == value) return;
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
            if (value.Equals(_imagePositionX)) return;
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
            if (value.Equals(_imagePositionY)) return;
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
            if (value.Equals(_imageScale)) return;
            _imageScale = value;
            Refresh();
        }
    }

    private Bitmap _renderedImage;

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