using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// A workspace groups the actual touch and rotary layouts (pages) for one area of a
/// <see cref="Profile"/> (issue #132). The page collections and their active-page
/// projections used to live directly on <see cref="LoupedeckConfig"/>; they were moved
/// here unchanged so that each profile can hold several independent workspaces. The old
/// root-level properties on <see cref="LoupedeckConfig"/> now forward to the active
/// workspace, so existing bindings and callers keep working.
/// </summary>
public partial class Workspace : ObservableObject
{
    public Workspace()
    {
        // The four page collections need setter-driven CollectionChanged wiring, so they
        // MUST be assigned via the generated setters (an inline `= new()` writes straight
        // to the backing field and skips the On…Changed hook). See the same gotcha
        // documented in LoupedeckConfig's constructor (bugs #775/#777).
        RotaryButtonPages = new();
        LeftRotaryButtonPages = new();
        RightRotaryButtonPages = new();
        TouchButtonPages = new();
    }

    /// <summary>Stable identity used by commands and context rules to target this workspace.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-assigned workspace name (e.g. "Scenes", "Audio Mixer").</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// Touch page to activate when this workspace becomes active. Per-workspace since #132
    /// (previously a single device-wide <c>StartupTouchPageIndex</c>).
    /// </summary>
    public int StartupTouchPageIndex { get; set; }

    // --- Shared / legacy rotary pages (devices without side strips) -----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; }

    partial void OnRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => RotaryButtonPages?.CollectionChanged -= OnRotaryPagesChanged;
    partial void OnRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => RotaryButtonPages?.CollectionChanged += OnRotaryPagesChanged;
    private void OnRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Deleting a non-last page leaves CurrentRotaryPageIndex unchanged, so the
        // index-driven [NotifyPropertyChangedFor] never fires even though the page
        // at that index is now a different object. Re-evaluate the projection here.
        OnPropertyChanged(nameof(RotaryPageLabel));
        OnPropertyChanged(nameof(CurrentRotaryButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(RotaryPageLabel))]
    public partial int CurrentRotaryPageIndex { get; set; } = -1;

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonPage =>
        (RotaryButtonPages != null &&
         CurrentRotaryPageIndex >= 0 &&
         CurrentRotaryPageIndex < RotaryButtonPages.Count)
            ? RotaryButtonPages[CurrentRotaryPageIndex]
            : null;

    /// <summary>"current / total" label for the rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RotaryPageLabel =>
        RotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentRotaryPageIndex + 1, 1, RotaryButtonPages.Count)} / {RotaryButtonPages.Count}"
            : "0 / 0";

    // --- Independent left/right rotary pages (devices with side strips) -------
    // Devices with side strips (Razer Stream Controller) page each dial column on
    // its own: LeftRotaryButtonPages / RightRotaryButtonPages each hold that side's
    // knobs (3 on the Razer, re-indexed 0-based per side). Devices without side
    // strips (Live S) leave these empty and keep using RotaryButtonPages (Both).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftRotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> LeftRotaryButtonPages { get; set; }

    partial void OnLeftRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => LeftRotaryButtonPages?.CollectionChanged -= OnLeftRotaryPagesChanged;
    partial void OnLeftRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => LeftRotaryButtonPages?.CollectionChanged += OnLeftRotaryPagesChanged;
    private void OnLeftRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LeftRotaryPageLabel));
        OnPropertyChanged(nameof(CurrentLeftRotaryButtonPage));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightRotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> RightRotaryButtonPages { get; set; }

    partial void OnRightRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => RightRotaryButtonPages?.CollectionChanged -= OnRightRotaryPagesChanged;
    partial void OnRightRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => RightRotaryButtonPages?.CollectionChanged += OnRightRotaryPagesChanged;
    private void OnRightRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RightRotaryPageLabel));
        OnPropertyChanged(nameof(CurrentRightRotaryButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentLeftRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(LeftRotaryPageLabel))]
    public partial int CurrentLeftRotaryPageIndex { get; set; } = -1;

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentRightRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(RightRotaryPageLabel))]
    public partial int CurrentRightRotaryPageIndex { get; set; } = -1;

    [JsonIgnore]
    public RotaryButtonPage CurrentLeftRotaryButtonPage =>
        (LeftRotaryButtonPages != null &&
         CurrentLeftRotaryPageIndex >= 0 &&
         CurrentLeftRotaryPageIndex < LeftRotaryButtonPages.Count)
            ? LeftRotaryButtonPages[CurrentLeftRotaryPageIndex]
            : null;

    [JsonIgnore]
    public RotaryButtonPage CurrentRightRotaryButtonPage =>
        (RightRotaryButtonPages != null &&
         CurrentRightRotaryPageIndex >= 0 &&
         CurrentRightRotaryPageIndex < RightRotaryButtonPages.Count)
            ? RightRotaryButtonPages[CurrentRightRotaryPageIndex]
            : null;

    /// <summary>"current / total" label for the left rotary pager (1-based).</summary>
    [JsonIgnore]
    public string LeftRotaryPageLabel =>
        LeftRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentLeftRotaryPageIndex + 1, 1, LeftRotaryButtonPages.Count)} / {LeftRotaryButtonPages.Count}"
            : "0 / 0";

    /// <summary>"current / total" label for the right rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RightRotaryPageLabel =>
        RightRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentRightRotaryPageIndex + 1, 1, RightRotaryButtonPages.Count)} / {RightRotaryButtonPages.Count}"
            : "0 / 0";

    // Strip rendering mode is per rotary page (see RotaryButtonPage.StripMode), not
    // global per side — each page on a column can independently be Segmented/FreeDraw.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TouchPageLabel))]
    public partial ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; }

    partial void OnTouchButtonPagesChanging(ObservableCollection<TouchButtonPage> value) => TouchButtonPages?.CollectionChanged -= OnTouchPagesChanged;
    partial void OnTouchButtonPagesChanged(ObservableCollection<TouchButtonPage> value) => TouchButtonPages?.CollectionChanged += OnTouchPagesChanged;
    private void OnTouchPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TouchPageLabel));
        OnPropertyChanged(nameof(CurrentTouchButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentTouchButtonPage))]
    [NotifyPropertyChangedFor(nameof(TouchPageLabel))]
    public partial int CurrentTouchPageIndex { get; set; } = -1;

    [JsonIgnore]
    public TouchButtonPage CurrentTouchButtonPage =>
        (TouchButtonPages != null &&
         CurrentTouchPageIndex >= 0 &&
         CurrentTouchPageIndex < TouchButtonPages.Count)
            ? TouchButtonPages[CurrentTouchPageIndex]
            : null;

    /// <summary>"current / total" label for the touch pager (1-based).</summary>
    [JsonIgnore]
    public string TouchPageLabel =>
        TouchButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentTouchPageIndex + 1, 1, TouchButtonPages.Count)} / {TouchButtonPages.Count}"
            : "0 / 0";
}
