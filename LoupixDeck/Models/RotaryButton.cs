using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

public partial class RotaryButton(int index,string rotaryLeftCommand, string rotaryRightCommand) : LoupedeckButton
{
    public int Index { get; } = index;

    /// <summary>
    /// Static label shown for this knob on the side strip (segmented mode). Empty by
    /// default, so configs written before strips load unchanged and blank segments
    /// stay blank. Persisted; additive — missing in old JSON simply keeps the default.
    /// </summary>
    public string DisplayText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                Refresh();
        }
    } = string.Empty;

    [ObservableProperty]
    public partial string RotaryLeftCommand { get; set; } = rotaryLeftCommand;

    [ObservableProperty]
    public partial string RotaryRightCommand { get; set; } = rotaryRightCommand;
}