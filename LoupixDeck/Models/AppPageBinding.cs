using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One app-switching rule: when the foreground window matches
/// <see cref="ProcessName"/> (and optionally contains <see cref="TitleContains"/>),
/// the deck switches to <see cref="TouchPageIndex"/> (and optionally
/// <see cref="RotaryPageIndex"/>). Indices are 0-based, matching <c>PageManager</c>.
/// INotifyPropertyChanged so the settings editor binds live to the shared config.
/// </summary>
[ObservableObject]
public sealed partial class AppPageBinding
{
    [ObservableProperty]
    public partial string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TitleContains { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TouchPageIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotarySelectionIndex))]
    public partial int? RotaryPageIndex { get; set; }

    /// <summary>
    /// ComboBox helper for the rotary selector: 0 = "(unchanged)", n = rotary page n-1.
    /// Maps to/from the nullable <see cref="RotaryPageIndex"/>. Not persisted.
    /// </summary>
    [JsonIgnore]
    public int RotarySelectionIndex
    {
        get => RotaryPageIndex is { } idx ? idx + 1 : 0;
        set => RotaryPageIndex = value <= 0 ? null : value - 1;
    }
}
