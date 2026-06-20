using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class RotaryButtonPage
{
    public RotaryButtonPage(int pageSize)
    {
        RotaryButtons = new ObservableCollection<RotaryButton>();

        for (var i = 0; i < pageSize; i++)
        {
            var newButton = new RotaryButton(i, string.Empty, string.Empty);
            RotaryButtons.Add(newButton);
        }
    }

    /// <summary>
    /// Which dial column this page belongs to. Defaults to <see cref="RotarySide.Both"/>
    /// so configs written before the side split (and devices without side strips,
    /// e.g. the Live S) keep the single-column behaviour. The v3→v4 migration tags
    /// Razer pages <see cref="RotarySide.Left"/> / <see cref="RotarySide.Right"/>.
    /// </summary>
    public RotarySide Side { get; set; } = RotarySide.Both;

    /// <summary>
    /// Optional user-assigned page name. Persisted; when empty the page falls back
    /// to its number, so configs written before naming existed load unchanged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageName))]
    public partial string Name { get; set; }

    [JsonIgnore]
    public string PageName => string.IsNullOrWhiteSpace(Name) ? $"Rotary Page: {Page}" : Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageName))]
    public partial int Page { get; set; }
    [JsonIgnore]
    [ObservableProperty]
    public partial bool Selected { get; set; }

    public ObservableCollection<RotaryButton> RotaryButtons { get; set; }

    /// <summary>
    /// Rendering mode of this page's side strip (Razer). Per page, not per device:
    /// each rotary page on a column independently chooses Segmented (auto dial labels)
    /// or FreeDraw (the <see cref="StripCanvas"/>). Additive — missing in older JSON
    /// defaults to <see cref="StripMode.Segmented"/>, preserving prior behaviour.
    /// </summary>
    [ObservableProperty]
    public partial StripMode StripMode { get; set; } = StripMode.Segmented;

    /// <summary>
    /// Free-draw canvas for this page's side strip: a 60×270 layer surface (image/
    /// text/symbol) edited like a touch button, shown when this page's
    /// <see cref="StripMode"/> is <see cref="StripMode.FreeDraw"/>. Null/absent in
    /// older configs and in segmented mode; created on demand by the editor.
    /// </summary>
    public TouchButton StripCanvas { get; set; }

    /// <summary>
    /// Per-segment tap commands for a <see cref="StripMode.FreeDraw"/> side strip: the
    /// strip is split into three equal vertical zones (top=0, middle=1, bottom=2) and a
    /// tap runs the matching command. Additive — null/short in older configs is treated
    /// as "no command", so configs written before this existed load unchanged. A null
    /// array round-trips to null.
    /// </summary>
    public string[] StripSegmentCommands { get; set; }

    /// <summary>Number of free-draw strip segments (matches the three side dials).</summary>
    public const int StripSegmentCount = 3;

    /// <summary>Reads the command of free-draw segment <paramref name="index"/> (0..2),
    /// tolerating a null/short backing array.</summary>
    public string GetStripSegmentCommand(int index)
    {
        var commands = StripSegmentCommands;
        return commands != null && index >= 0 && index < commands.Length ? commands[index] : null;
    }

    /// <summary>Writes the command of free-draw segment <paramref name="index"/> (0..2),
    /// lazily allocating/growing the backing array to length 3 so older configs and a
    /// null array stay valid.</summary>
    public void SetStripSegmentCommand(int index, string value)
    {
        if (index < 0 || index >= StripSegmentCount) return;

        if (StripSegmentCommands == null || StripSegmentCommands.Length < StripSegmentCount)
        {
            var grown = new string[StripSegmentCount];
            if (StripSegmentCommands != null)
                Array.Copy(StripSegmentCommands, grown, StripSegmentCommands.Length);
            StripSegmentCommands = grown;
        }

        StripSegmentCommands[index] = value;
    }


    /// <summary>
    /// Id of the side-strip provider bound when <see cref="StripMode"/> is
    /// <see cref="StripMode.PluginOverride"/>. Persisted; null/absent in older configs
    /// and in non-plugin modes. An orphan id (the plugin is not installed) falls back
    /// to segmented rendering at runtime, and the id is preserved so re-installing the
    /// plugin restores the binding.
    /// </summary>
    [ObservableProperty]
    public partial string StripPluginId { get; set; }

    // Pre/Post-command wraps applied per input type when a button on this page fires.
    public CommandWrap SimpleButtonWrap { get; set; } = new();
    public CommandWrap KnobLeftWrap { get; set; } = new();
    public CommandWrap KnobRightWrap { get; set; } = new();
    public CommandWrap KnobPressWrap { get; set; } = new();
}