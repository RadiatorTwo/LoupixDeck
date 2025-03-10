using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LoupixDeck.Models;

public class TouchButton(int index) : LoupedeckButton
{
    public int Index { get; init; } = index;

    private string _text;
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

    private bool _textCentered;
    public bool TextCentered
    {
        get => _textCentered;
        set
        {
            if (value == _textCentered) return;
            _textCentered = value;
            //OnPropertyChanged(nameof(TextCentered));
            Refresh();
        }
    }

    private int _textSize;
    public int TextSize
    {
        get => _textSize;
        set
        {
            if (value == _textSize) return;
            _textSize = value;
            //OnPropertyChanged(nameof(TextSize));
            Refresh();
        }
    }

    private Point _textPosition;
    public Point TextPosition
    {
        get => _textPosition;
        set
        {
            if (value.Equals(_textPosition)) return;
            _textPosition = value;
            //OnPropertyChanged(nameof(TextPosition));
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
            //OnPropertyChanged(nameof(Image));
            Refresh();
        }
    }

    private Color _textColor;
    public Color TextColor
    {
        get => _textColor;
        set
        {
            if (value.Equals(_textColor)) return;
            _textColor = value;
            //OnPropertyChanged(nameof(TextColor));
            Refresh();
        }
    }

    private Color _backColor;
    public Color BackColor
    {
        get => _backColor;
        set
        {
            if (_backColor == value) return;
            _backColor = value;
            //OnPropertyChanged(nameof(BackColor));
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