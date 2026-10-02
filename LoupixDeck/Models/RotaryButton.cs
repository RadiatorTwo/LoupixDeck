using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public class RotaryButton(int index,string rotaryLeftCommand, string rotaryRightCommand) : LoupedeckButton
{
    public int Index { get; } = index;

    private string _displayText = string.Empty;

    /// <summary>
    /// Static label shown for this knob on the side strip (segmented mode). Empty by
    /// default, so configs written before strips load unchanged and blank segments
    /// stay blank. Persisted; additive — missing in old JSON simply keeps the default.
    /// </summary>
    public string DisplayText
    {
        get => _displayText;
        set
        {
            if (_displayText == value) return;
            _displayText = value;
            OnPropertyChanged(nameof(DisplayText));
            Refresh();
        }
    }

    private string _rotaryLeftCommand = rotaryLeftCommand;
    private string _rotaryRightCommand = rotaryRightCommand;
    
    public string RotaryLeftCommand
    {
        get => _rotaryLeftCommand;
        set
        {
            if (value == _rotaryLeftCommand) return;
            _rotaryLeftCommand = value;
            OnPropertyChanged(nameof(RotaryLeftCommand));
        }
    }

    public string RotaryRightCommand
    {
        get => _rotaryRightCommand;
        set
        {
            if (value == _rotaryRightCommand) return;
            _rotaryRightCommand = value;
            OnPropertyChanged(nameof(RotaryRightCommand));
        }
    }

    private SKBitmap _renderedImage;

    // Retired, not disposed on swap: the UI preview converter may still be copying the
    // previous bitmap. Same bounded-generations scheme as TouchButton.RenderedImage.
    private readonly Queue<SKBitmap> _retiredImages = new();
    private const int RetainedRenderedGenerations = 3;

    /// <summary>
    /// Last frame drawn to this dial's own screen — only the Loupedeck CT's centre wheel has
    /// one — mirrored so the device view can show it. Null for every other dial. Runtime-only.
    /// </summary>
    [JsonIgnore]
    public SKBitmap RenderedImage
    {
        get => _renderedImage;
        set
        {
            if (ReferenceEquals(value, _renderedImage)) return;

            lock (SkiaRenderGate.Sync)
            {
                var old = _renderedImage;
                _renderedImage = value;
                if (old != null)
                    _retiredImages.Enqueue(old);
                while (_retiredImages.Count > RetainedRenderedGenerations)
                    _retiredImages.Dequeue().Dispose();
            }

            OnPropertyChanged(nameof(RenderedImage));
        }
    }
}
