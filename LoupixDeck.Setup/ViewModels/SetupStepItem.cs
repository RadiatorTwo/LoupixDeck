using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Setup.ViewModels;

/// <summary>Display state of one timeline step.</summary>
public enum StepState
{
    Pending,
    Active,
    Done,
    Failed
}

/// <summary>
/// One row in the running / finished timeline: a fixed label and its stable <see cref="SetupSteps"/>
/// key, plus the mutable <see cref="State"/> that drives its glyph and colour.
/// </summary>
public sealed partial class SetupStepItem : ObservableObject
{
    public SetupStepItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial StepState State { get; set; }

    /// <summary>Leading glyph shown before the label: ✓ done, → active, ✕ failed, • pending.</summary>
    public string Glyph => State switch
    {
        StepState.Done => "✓",
        StepState.Active => "→",
        StepState.Failed => "✕",
        _ => "•"
    };
}
