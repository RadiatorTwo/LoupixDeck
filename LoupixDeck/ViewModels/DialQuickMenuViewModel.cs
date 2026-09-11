using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.DialPresets;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Backs the dial section of a device button's context menu: the command catalogue a dial can run,
/// the presets it can be loaded from, and the writes that bind a gesture.
/// </summary>
/// <remarks>
/// The catalogue is built once per device and kept, because the menu is opened over and over and
/// rebuilding it — including every plugin's contribution — on each right-click is exactly the cost
/// the menu is meant to avoid. It is the same tree the rotary button editor shows, built for
/// <see cref="ButtonTargets.RotaryEncoder"/>, so the quick menu can never offer a command the full
/// editor does not.
/// </remarks>
public sealed class DialQuickMenuViewModel
{
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IDialPresetCatalog _presets;

    private bool _loadStarted;

    // Command string -> the catalogue leaf that produces it. Built on demand and dropped whenever
    // the catalogue changes, which it does when a slow plugin's group arrives after the tree was
    // first shown.
    private Dictionary<string, MenuEntry> _byCommand;

    public DialQuickMenuViewModel(IMenuTreeBuilder menuTreeBuilder, ICommandBuilder commandBuilder,
        IDialPresetCatalog presets)
    {
        _menuTreeBuilder = menuTreeBuilder;
        _commandBuilder = commandBuilder;
        _presets = presets;

        Catalogue.CollectionChanged += OnCatalogueChanged;
    }

    /// <summary>
    /// Drops the index whenever the catalogue changes. A group's own children are watched too: a
    /// plugin that finishes loading after the tree was first built merges its commands into an
    /// existing group rather than adding a new one, which the collection itself never reports.
    /// </summary>
    private void OnCatalogueChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (MenuEntry group in e.OldItems ?? (System.Collections.IList)Array.Empty<MenuEntry>())
            group.Children.CollectionChanged -= OnGroupChanged;

        foreach (MenuEntry group in e.NewItems ?? (System.Collections.IList)Array.Empty<MenuEntry>())
            group.Children.CollectionChanged += OnGroupChanged;

        _byCommand = null;
    }

    private void OnGroupChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => _byCommand = null;

    /// <summary>The command groups a dial can be given, in catalogue order.</summary>
    public ObservableCollection<MenuEntry> Catalogue { get; } = [];

    /// <summary>True once the catalogue holds something. Until then the menu says it is loading
    /// rather than showing an empty command list.</summary>
    public bool IsReady => Catalogue.Count > 0;

    /// <summary>The built-in and user presets available on this device.</summary>
    public IReadOnlyList<DialPreset> Presets => _presets.Presets;

    /// <summary>
    /// Builds the catalogue. Idempotent: the first call does the work and every later one returns
    /// immediately. Started in the background once the device is up, so the first right-click on a
    /// dial already has its commands.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loadStarted)
            return;

        _loadStarted = true;
        await _menuTreeBuilder.BuildInto(Catalogue, ButtonTargets.RotaryEncoder);
    }

    /// <summary>
    /// Binds <paramref name="entry"/> to one gesture of <paramref name="dial"/>. Returns false when
    /// the entry produces no command, which is what an entry the registry does not know does.
    /// </summary>
    /// <remarks>
    /// The command string comes from the same builder the panel and the button editors use, so a
    /// command's declared parameter defaults are applied identically however it was assigned. A
    /// parameterized command lands on its defaults here and is refined in the full editor.
    /// </remarks>
    public bool AssignGesture(RotaryButton dial, RotaryAction gesture, MenuEntry entry)
    {
        if (dial == null || entry == null)
            return false;

        // An entry that fills every gesture at once is a preset in catalogue form; binding it to a
        // single gesture would silently drop two thirds of it.
        if (entry.RotaryGroup is { Count: > 0 } group)
            return ApplyRotaryGroup(dial, group, entry.Name);

        string command = _commandBuilder.CreateCommandFromMenuEntry(entry);
        if (string.IsNullOrEmpty(command))
            return false;

        dial.SetCommand(gesture, command);

        if (string.IsNullOrWhiteSpace(dial.DisplayText))
            dial.DisplayText = entry.Name ?? string.Empty;

        return true;
    }

    /// <summary>Applies a preset, filling every gesture it names and leaving the rest alone.</summary>
    public bool ApplyPreset(RotaryButton dial, DialPreset preset)
    {
        if (dial == null || preset == null)
            return false;

        return ApplyRotaryGroup(dial, preset.ToRotaryGroup(), preset.Name);
    }

    /// <summary>
    /// Clears one gesture. When that was the dial's last binding its side-strip label goes too:
    /// the label is the only place a dial's assignment shows on the hardware, so leaving it behind
    /// would announce a command that is no longer there.
    /// </summary>
    public void ClearGesture(RotaryButton dial, RotaryAction gesture)
    {
        if (dial == null)
            return;

        dial.SetCommand(gesture, string.Empty);

        if (dial.IsEmpty())
            dial.DisplayText = string.Empty;
    }

    /// <summary>
    /// The catalogue entry a gesture's command came from, or null when it matches none — a command
    /// edited by hand, or a chain the quick menu cannot express. Matched on the exact string the
    /// builder produces for each leaf rather than on a prefix, so a parameterized command is never
    /// confused with the generic entry it was built from.
    /// </summary>
    public MenuEntry FindAssigned(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        _byCommand ??= BuildIndex();
        return _byCommand.GetValueOrDefault(command);
    }

    /// <summary>
    /// Indexes the catalogue by the command string each leaf produces. Built once per catalogue
    /// rather than per menu: resolving three gestures by walking every leaf of every plugin group
    /// is the kind of work a quick menu must not do on each right-click. The first leaf wins when
    /// two produce the same string, which is the one the user sees ticked.
    /// </summary>
    private Dictionary<string, MenuEntry> BuildIndex()
    {
        Dictionary<string, MenuEntry> index = new(StringComparer.Ordinal);

        foreach (MenuEntry group in Catalogue)
            AddToIndex(index, group);

        return index;
    }

    private void AddToIndex(Dictionary<string, MenuEntry> index, MenuEntry entry)
    {
        foreach (MenuEntry child in entry.Children)
        {
            if (!string.IsNullOrEmpty(child.Command))
            {
                string command = _commandBuilder.CreateCommandFromMenuEntry(child);
                if (!string.IsNullOrEmpty(command))
                    index.TryAdd(command, child);
            }

            AddToIndex(index, child);
        }
    }

    /// <summary>The preset the dial is currently configured from, or null. A preset matches when
    /// every gesture it names holds exactly its command.</summary>
    public DialPreset FindAssignedPreset(RotaryButton dial)
    {
        if (dial == null || dial.IsEmpty())
            return null;

        return Presets.FirstOrDefault(preset => Matches(dial, preset));
    }

    private static bool Matches(RotaryButton dial, DialPreset preset)
    {
        foreach ((RotaryAction action, string command) in preset.ToRotaryGroup())
        {
            if (!string.Equals(dial.GetCommand(action), command, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool ApplyRotaryGroup(RotaryButton dial,
        IReadOnlyDictionary<RotaryAction, string> group, string label)
    {
        if (group == null || group.Count == 0)
            return false;

        foreach ((RotaryAction action, string command) in group)
            dial.SetCommand(action, command);

        dial.DisplayText = label ?? string.Empty;
        return true;
    }
}
