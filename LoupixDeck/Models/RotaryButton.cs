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

    /// <summary>
    /// <see cref="TouchButton.Index"/> of a wheel's <see cref="Canvas"/>. Not a grid slot; it only
    /// identifies the canvas in snapshots and logs.
    /// </summary>
    public const int WheelCanvasIndex = 1000;

    private TouchButton _canvas;

    /// <summary>
    /// Custom layout of this dial's own screen (the Loupedeck CT's centre wheel), as a layer
    /// canvas edited like a touch button. Null means the automatic layout: the label, and the
    /// adjustment command's arc and value when it has one, drawn the way LoupixDeck always has.
    /// Persisted only when set, so configs without one load and save unchanged. Each wheel mode
    /// has its own.
    /// </summary>
    [JsonProperty("Canvas", NullValueHandling = NullValueHandling.Ignore)]
    public TouchButton Canvas
    {
        get => _canvas;
        set
        {
            if (ReferenceEquals(_canvas, value)) return;
            _canvas = value;
            OnPropertyChanged(nameof(Canvas));
            OnPropertyChanged(nameof(HasCustomCanvas));
        }
    }

    /// <summary>True when <see cref="Canvas"/> replaces the automatic layout.</summary>
    [JsonIgnore]
    public bool HasCustomCanvas => _canvas != null;

    /// <summary>
    /// Post-load wiring for <see cref="Canvas"/>: the deserializer builds its layers without
    /// going through the setters, so their change handlers have to be attached afterwards, the
    /// same as <see cref="TouchButton.RewireLayerHandlers"/> for grid keys.
    /// </summary>
    public void RewireAfterLoad() => _canvas?.RewireLayerHandlers();

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
