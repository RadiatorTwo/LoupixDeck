using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.Profiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.ActionPanel;

/// <summary>
/// One row in the side panel, and the payload a drag carries from it. Rows are the only thing the
/// panel hands to the rest of the app: the drag machine picks one up, and the device view model
/// decides what assigning it means for the button it was dropped on.
/// </summary>
public abstract partial class PanelItemViewModel : ViewModelBase
{
    /// <summary>Row caption, and the caption written onto the button when the item is assigned.</summary>
    public abstract string Title { get; }

    /// <summary>Optional second line — where an application came from, what a command does.</summary>
    public virtual string Subtitle => null;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>Material Design glyph for rows that have no bitmap of their own. Empty for rows
    /// whose icon is an image.</summary>
    public string Glyph { get; protected init; } = string.Empty;

    public bool HasGlyph => !string.IsNullOrEmpty(Glyph);

    /// <summary>
    /// Row bitmap, filled in after the row is already on screen for items whose icon has to be
    /// extracted from disk. Null until then, and for rows that only ever show a glyph.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial Bitmap Icon { get; set; }

    public bool HasIcon => Icon != null;

    /// <summary>True for a row the user put there themselves and can take away again. A scanned
    /// application would only come back on the next scan, so removing one is not offered.</summary>
    public bool CanRemove { get; init; }

    /// <summary>True for a preset the user created, which can be renamed and deleted. Separate from
    /// <see cref="CanRemove"/>: the two offer different menu entries, and a row must never show one
    /// that does nothing to it.</summary>
    public bool CanEdit { get; init; }

    /// <summary>Menu entry that links the row's application to the active profile. Declared here so
    /// the shared row template binds without casting; only application rows ever show it.</summary>
    public virtual bool ShowLinkEntry => false;

    /// <summary>Menu entry that removes the active profile's link to the row's application.</summary>
    public virtual bool ShowUnlinkEntry => false;

    /// <summary>Disabled menu entry explaining that the row's application cannot be linked.</summary>
    public virtual bool ShowCannotLinkEntry => false;
}

/// <summary>An installed application: assigning it puts its launch command and its icon on the button.</summary>
public sealed partial class AppPanelItemViewModel(InstalledApp app) : PanelItemViewModel
{
    public InstalledApp App { get; } = app;

    /// <summary>True when this application's process is the active profile's plain link, so the
    /// row menu offers removing it instead of creating it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLinkEntry))]
    [NotifyPropertyChangedFor(nameof(ShowUnlinkEntry))]
    public partial bool IsLinkedToActiveProfile { get; set; }

    /// <summary>False on platforms without foreground-app detection. Set with the link flag.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLinkEntry))]
    [NotifyPropertyChangedFor(nameof(ShowUnlinkEntry))]
    [NotifyPropertyChangedFor(nameof(ShowCannotLinkEntry))]
    public partial bool IsLinkingSupported { get; set; }

    /// <summary>Whether a rule can target this application's process at all.</summary>
    public bool CanLinkToProfile => ProfileAppLink.CanLinkProcess(App.ProcessName);

    public override bool ShowLinkEntry => IsLinkingSupported && CanLinkToProfile && !IsLinkedToActiveProfile;

    public override bool ShowUnlinkEntry => IsLinkingSupported && CanLinkToProfile && IsLinkedToActiveProfile;

    public override bool ShowCannotLinkEntry => IsLinkingSupported && !CanLinkToProfile;

    public override string Title => App.Name;

    public override string Subtitle => App.SourceLabel;
}

/// <summary>
/// One command from the shared menu catalogue — the same tree the command picker shows, so the
/// panel offers core commands, user macros, profile activation and every loaded plugin without a
/// second list to keep in step.
/// </summary>
public sealed class ActionPanelItemViewModel : PanelItemViewModel
{
    /// <summary>The catalogue leaf. Carries the command name and the parameters the command builder
    /// substitutes, so the panel never composes a command string itself.</summary>
    public MenuEntry Entry { get; }

    /// <summary>
    /// The glyph resolved back to a <see cref="SymbolLibrary"/> id, so the same icon the row shows
    /// can be drawn on the deck. Empty when the command's glyph is outside the curated subset.
    /// </summary>
    public string SymbolId { get; }

    public ActionPanelItemViewModel(MenuEntry entry, string fallbackGlyph = null)
    {
        Entry = entry;
        Glyph = string.IsNullOrEmpty(entry.Icon) ? (fallbackGlyph ?? string.Empty) : entry.Icon;
        SymbolId = SymbolLibrary.TryGetByGlyph(Glyph, out SymbolDefinition definition)
            ? definition.Id
            : string.Empty;
    }

    public override string Title => Entry.Name;

    public override string Subtitle => Entry.Description;
}

/// <summary>
/// A dial preset: assigning it fills all three gestures of a dial at once. Rejected on every other
/// button type, which has no gestures to fill.
/// </summary>
public sealed class DialPresetPanelItemViewModel : PanelItemViewModel
{
    public DialPreset Preset { get; }

    public DialPresetPanelItemViewModel(DialPreset preset)
    {
        Preset = preset;
        Glyph = string.IsNullOrEmpty(preset.Glyph) ? DialPreset.DefaultGlyph : preset.Glyph;
        CanEdit = !preset.IsBuiltIn;
    }

    public override string Title => Preset.Name;

    public override string Subtitle => Preset.IsBuiltIn ? "Built-in" : "Your preset";
}
