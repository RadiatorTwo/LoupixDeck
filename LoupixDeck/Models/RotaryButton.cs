using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public partial class RotaryButton(int index,string rotaryLeftCommand, string rotaryRightCommand) : LoupedeckButton
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
    public void RewireAfterLoad()
    {
        _canvas?.RewireLayerHandlers();
        SplitCanvas?.RewireLayerHandlers();
        EnsureSegments();
        foreach (var segment in Segments)
            segment?.RewireAfterLoad();
    }

    // ───────── Split wheel screen (PluginSdk #30) ─────────

    /// <summary>
    /// <see cref="TouchButton.Index"/> of the first segment canvas; segment <c>n</c> uses this
    /// plus <c>n</c>. Like <see cref="WheelCanvasIndex"/>, only an identifier.
    /// </summary>
    public const int WheelSegmentCanvasIndex = 1001;

    /// <summary><see cref="TouchButton.Index"/> of <see cref="SplitCanvas"/>; only an identifier.</summary>
    public const int WheelSplitCanvasIndex = 1010;

    /// <summary>
    /// The layers a split wheel shows on all segments: a canvas the size of the whole wheel screen,
    /// drawn unclipped, each layer behind the segments or over them
    /// (<see cref="Layers.LayerBase.BehindSegments"/>). Its background colour, when on, fills the
    /// screen behind everything. Null until the appearance editor creates it; persisted only when
    /// set. Not drawn while the wheel is unsplit.
    /// </summary>
    [ObservableProperty]
    [JsonProperty("SplitCanvas", NullValueHandling = NullValueHandling.Ignore)]
    public partial TouchButton SplitCanvas { get; set; }

    /// <summary>
    /// How this wheel mode splits the wheel screen into pressable segments. Only the Loupedeck
    /// CT's centre wheel uses it. Written only when split, so configs without a split load and
    /// save unchanged; missing in older JSON means <see cref="WheelSplitMode.None"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplit))]
    public partial WheelSplitMode SplitMode { get; set; }

    public bool ShouldSerializeSplitMode() => SplitMode != WheelSplitMode.None;

    partial void OnSplitModeChanged(WheelSplitMode value) => EnsureSegments();

    /// <summary>
    /// The segments of a split wheel, in the order <see cref="WheelSplitGeometry"/> documents. Up
    /// to <see cref="WheelSplitGeometry.MaxSegments"/> are kept even when the current mode uses
    /// fewer, so switching from four segments to two and back keeps the hidden ones. Written only
    /// when there is at least one.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WheelSegment> Segments { get; set; } = [];

    public bool ShouldSerializeSegments() => Segments is { Count: > 0 };

    /// <summary>
    /// The commands bound to the dial itself, in resolution order: left, right, then the press
    /// command. A split wheel's press lives on its segments, so its own press command is left out.
    /// </summary>
    public string[] DialCommands() =>
        IsSplit ? [RotaryLeftCommand, RotaryRightCommand] : [RotaryLeftCommand, RotaryRightCommand, Command];

    /// <summary>True when the wheel screen is split into segments.</summary>
    [JsonIgnore]
    public bool IsSplit => SplitMode != WheelSplitMode.None;

    /// <summary>Grows <see cref="Segments"/> to the current mode's segment count. Never shrinks it.</summary>
    public void EnsureSegments()
    {
        Segments ??= [];
        for (int i = 0; i < Segments.Count; i++)
            Segments[i] ??= new WheelSegment();

        int needed = WheelSplitGeometry.SegmentCount(SplitMode);
        while (Segments.Count < needed)
            Segments.Add(new WheelSegment());
    }

    /// <summary>
    /// The segments the current mode shows, in order. Empty when the wheel is not split. A
    /// snapshot, safe to enumerate while the editor changes the list.
    /// </summary>
    public IReadOnlyList<WheelSegment> ActiveSegments()
    {
        int count = WheelSplitGeometry.SegmentCount(SplitMode);
        if (count == 0) return [];
        var segments = Segments;
        return segments == null ? [] : segments.Take(count).Select(s => s ?? new WheelSegment()).ToArray();
    }

    /// <summary>
    /// The SDK description of segment <paramref name="index"/>, handed to plugins as
    /// <c>CommandContext.WheelSegment</c>.
    /// </summary>
    public LoupixDeck.PluginSdk.WheelSegmentInfo SegmentInfo(int index) =>
        new((LoupixDeck.PluginSdk.WheelSplitMode)(int)SplitMode, index, WheelSplitGeometry.SegmentCount(SplitMode));

    /// <summary>
    /// The layer canvases currently drawn on the wheel screen, each with the segment it belongs to
    /// (null for the unsplit wheel's own <see cref="Canvas"/> and for <see cref="SplitCanvas"/>).
    /// Canvases of hidden segments and, while split, the unsplit canvas are not included.
    /// </summary>
    public IReadOnlyList<(TouchButton Canvas, WheelSegment Segment, int Index)> VisibleCanvases()
    {
        if (!IsSplit)
            return _canvas == null ? [] : [(_canvas, null, -1)];

        var result = new List<(TouchButton, WheelSegment, int)>();
        if (SplitCanvas is { } shared)
            result.Add((shared, null, -1));
        var segments = ActiveSegments();
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Canvas is { } canvas)
                result.Add((canvas, segments[i], i));
        }

        return result;
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
