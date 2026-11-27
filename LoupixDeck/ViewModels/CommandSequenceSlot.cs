using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One named command sequence (e.g. a rotary knob's "Rotate Left" chain) shown as a
/// pipeline strip of editable <see cref="CommandSegment"/> chips. The raw, persisted
/// command string is the source of truth and is read/written through the supplied
/// delegates; this collection is a view over it that is recomposed on every edit.
///
/// Mirrors the command-chain logic of <c>TouchButtonSettingsViewModel</c> but is
/// self-contained so several slots can coexist in a single editor dialog.
/// </summary>
public class CommandSequenceSlot : ViewModelBase, ICommandChain
{
    private readonly ICommandBuilder _commandBuilder;
    private readonly ICommandRegistry _commandRegistry;
    private readonly Services.Companion.ICommandLockService _commandLock;
    private readonly IDialogService _dialogService;
    private readonly Services.Commands.ICommandParameterOptions _parameterOptions;
    private readonly Func<CommandBinding> _read;
    private readonly Action<CommandBinding> _write;

    /// <summary>Header shown above the strip, e.g. "Rotate Left".</summary>
    public string Title { get; }

    /// <summary>The slot's command chain as individual, editable chips.</summary>
    public ObservableCollection<CommandSegment> Commands { get; } = [];

    public IRelayCommand ClearCommandCommand => field ??= Relay.Create(ClearCommandOnly);

    public CommandSequenceSlot(
        string title,
        ICommandBuilder commandBuilder,
        ICommandRegistry commandRegistry,
        Services.Companion.ICommandLockService commandLock,
        IDialogService dialogService,
        Func<CommandBinding> read,
        Action<CommandBinding> write,
        Services.Commands.ICommandParameterOptions parameterOptions = null)
    {
        Title = title;
        _commandBuilder = commandBuilder;
        _commandRegistry = commandRegistry;
        _commandLock = commandLock;
        _dialogService = dialogService;
        _parameterOptions = parameterOptions;
        _read = read;
        _write = write;

        // Keep the 1-based chip numbers in sync with the collection.
        Commands.CollectionChanged += (_, _) => RenumberSegments();

        LoadSegments();
    }

    private bool _isActive;

    /// <summary>True when this slot is the target for double-click-to-append from
    /// the command tree. Exactly one slot is active at a time in the editor.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>True when the slot has a non-empty command assigned.</summary>
    public bool HasCommand => !CommandBinding.IsNullOrEmpty(_read());

    /// <summary>The slot's command chain as stored on the button.</summary>
    public string CommandText => _read()?.ToLegacyString();

    /// <summary>Parses the persisted command string into editable chips. Does not write
    /// back — opening (and closing without edits) leaves the string byte-for-byte unchanged.</summary>
    private void LoadSegments()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        foreach (CommandCall call in _read() ?? CommandBinding.Empty)
            Commands.Add(CreateSegment(call));
    }

    private CommandSegment CreateSegment(CommandCall call)
    {
        var info = _commandRegistry.Get(call.Name)?.Info;
        var owner = info == null ? _commandRegistry.GetMissingPluginOwner(call.Name) : null;
        var segment = CommandSegment.Create(_commandBuilder, _dialogService, info, call, owner,
            info != null ? _commandLock.GetLockHint(call) : null, _parameterOptions);
        segment.Changed += OnSegmentChanged;
        return segment;
    }

    private void OnSegmentChanged(object sender, EventArgs e) => RebuildCommandString();

    private void RenumberSegments()
    {
        for (var i = 0; i < Commands.Count; i++)
            Commands[i].Position = i + 1;
    }

    /// <summary>Recomposes the persisted chain from the current chip order/values and writes it
    /// back through the slot's setter.</summary>
    private void RebuildCommandString()
    {
        CommandBinding chain = new(Commands.Where(s => !s.IsBlank).Select(s => s.Call));

        _write(chain.IsEmpty ? string.Empty : chain);
        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Appends a command (double-click in the tree) to the end of the chain.</summary>
    public void InsertCommand(MenuEntry menuEntry) => InsertCommandAt(menuEntry, Commands.Count);

    /// <summary>Replaces the slot's whole chain (used when applying a command group). The chips
    /// are rebuilt from it so they stay individually editable afterwards.</summary>
    public void SetCommand(CommandBinding command)
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        _write(CommandBinding.IsNullOrEmpty(command) ? string.Empty : command);

        foreach (CommandCall call in _read() ?? CommandBinding.Empty)
            Commands.Add(CreateSegment(call));

        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Inserts a command (drag from the tree) at the given chip index.</summary>
    public void InsertCommandAt(MenuEntry menuEntry, int index)
    {
        if (menuEntry == null) return;

        var call = _commandBuilder.CreateCallFromMenuEntry(menuEntry);
        if (call == null) return;

        var wasEmpty = Commands.Count == 0;
        InsertCallAt(call, index);
        CommandInserted?.Invoke(menuEntry, wasEmpty);
    }

    /// <summary>Inserts an already formatted command (e.g. an application's launch command) at the
    /// given chip index. Returns false when there was nothing to insert.</summary>
    public bool InsertRawAt(string raw, int index)
    {
        // A formatted command may hold a whole chain; each of its commands becomes a chip.
        int inserted = 0;
        foreach (CommandCall call in CommandBinding.FromLegacy(raw))
        {
            if (InsertCallAt(call, index + inserted))
                inserted++;
        }

        return inserted > 0;
    }

    public bool InsertCallAt(CommandCall call, int index)
    {
        if (call == null || (call.IsText && string.IsNullOrWhiteSpace(call.Text))) return false;

        index = Math.Clamp(index, 0, Commands.Count);
        Commands.Insert(index, CreateSegment(call));
        RebuildCommandString();
        return true;
    }

    /// <summary>Raised after a command was inserted from the picker; the flag says whether the
    /// chain was empty before, i.e. whether this is the slot's first command.</summary>
    public event Action<MenuEntry, bool> CommandInserted;

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
