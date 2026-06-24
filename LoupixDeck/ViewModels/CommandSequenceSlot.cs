using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One named command sequence (e.g. a rotary knob's "Rotate Left" chain) shown as a
/// pipeline strip of editable <see cref="CommandSegment"/> chips.
/// </summary>
/// <remarks>
/// <para>
/// The raw, persisted command string is the source of truth and is read/written through the supplied
/// delegates; this collection is a view over it that is recomposed on every edit.
/// </para>
/// <para>
/// Mirrors the command-chain logic of <see cref="TouchButtonSettingsViewModel"/> but is
/// self-contained so several slots can coexist in a single editor dialog.
/// </para>
/// </remarks>
public partial class CommandSequenceSlot : ViewModelBase
{
    private readonly ICommandBuilder _commandBuilder;
    private readonly ICommandRegistry _commandRegistry;
    private readonly Func<string> _read;
    private readonly Action<string> _write;

    /// <summary>Header shown above the strip, e.g. "Rotate Left".</summary>
    public string Title { get; }

    /// <summary>The slot's command chain as individual, editable chips.</summary>
    public ObservableCollection<CommandSegment> Commands { get; } = [];

    public IRelayCommand ClearCommandCommand => field ??= Relay.Create(ClearCommandOnly);

    public CommandSequenceSlot(
        string title,
        ICommandBuilder commandBuilder,
        ICommandRegistry commandRegistry,
        Func<string> read,
        Action<string> write)
    {
        Title = title;
        _commandBuilder = commandBuilder;
        _commandRegistry = commandRegistry;
        _read = read;
        _write = write;

        // Keep the 1-based chip numbers in sync with the collection.
        Commands.CollectionChanged += (_, _) => RenumberSegments();

        LoadSegments();
    }

    /// <summary>True when this slot is the target for double-click-to-append from
    /// the command tree. Exactly one slot is active at a time in the editor.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>True when the slot has a non-empty command assigned.</summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(_read());

    /// <summary>Parses the persisted command string into editable chips. Does not write
    /// back — opening (and closing without edits) leaves the string byte-for-byte unchanged.</summary>
    private void LoadSegments()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        foreach (var raw in CommandStringParser.SplitChain(_read()))
            Commands.Add(CreateSegment(raw));
    }

    private CommandSegment CreateSegment(string raw)
    {
        var name = CommandStringParser.GetName(raw);
        var info = _commandRegistry.Get(name)?.Info;
        var segment = CommandSegment.Create(_commandBuilder, info, raw);
        segment.Changed += OnSegmentChanged;
        return segment;
    }

    private void OnSegmentChanged(object sender, EventArgs e) => RebuildCommandString();

    private void RenumberSegments()
    {
        for (var i = 0; i < Commands.Count; i++)
            Commands[i].Position = i + 1;
    }

    /// <summary>Recomposes the persisted <c>&amp;&amp;</c>-joined command string from the
    /// current chip order/values and writes it back through the slot's setter.</summary>
    private void RebuildCommandString()
    {
        var joined = string.Join(" && ",
            Commands.Select(s => s.Raw).Where(r => !string.IsNullOrWhiteSpace(r)));

        _write(string.IsNullOrWhiteSpace(joined) ? string.Empty : joined);
        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Appends a command (double-click in the tree) to the end of the chain.</summary>
    public void InsertCommand(MenuEntry menuEntry) => InsertCommandAt(menuEntry, Commands.Count);

    /// <summary>Inserts a command (drag from the tree) at the given chip index.</summary>
    public void InsertCommandAt(MenuEntry menuEntry, int index)
    {
        if (menuEntry == null) return;

        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrWhiteSpace(formattedCommand)) return;

        index = Math.Clamp(index, 0, Commands.Count);
        Commands.Insert(index, CreateSegment(formattedCommand));
        RebuildCommandString();
    }

    public void RemoveSegment(CommandSegment segment)
    {
        if (segment == null || !Commands.Remove(segment)) return;
        segment.Changed -= OnSegmentChanged;
        RebuildCommandString();
    }

    public void MoveSegment(int from, int to)
    {
        if (from < 0 || from >= Commands.Count) return;
        to = Math.Clamp(to, 0, Commands.Count - 1);
        if (from == to) return;
        Commands.Move(from, to);
        RebuildCommandString();
    }

    public void ClearCommandOnly()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();
        _write(string.Empty);
        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Detaches chip handlers — called when the dialog closes.</summary>
    public void Cleanup()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
    }
}
