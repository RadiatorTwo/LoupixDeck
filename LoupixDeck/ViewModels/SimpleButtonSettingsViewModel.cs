using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel : DialogViewModelBase<SimpleButton, DialogResult>, IAsyncInitViewModel
{
    public override void Initialize(SimpleButton parameter)
    {
        ButtonData = parameter;

        LoadSegments();

        OnPropertyChanged(nameof(ButtonLabel));
        NotifyCommandChanged();
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;

    public SimpleButton ButtonData { get; set; }

    /// <summary>Friendly label for the physical button id, displayed 1-based
    /// (BUTTON0 → "Button 1"). The underlying enum stays 0-based.</summary>
    public string ButtonLabel
    {
        get
        {
            var id = ButtonData?.Id.ToString();
            if (id != null && id.StartsWith("BUTTON") && int.TryParse(id["BUTTON".Length..], out var n))
                return $"Button {n + 1}";
            return id?.Replace("BUTTON", "Button ") ?? "Button";
        }
    }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    /// <summary>The button's command chain as individual, editable cards. The raw
    /// <see cref="LoupedeckButton.Command"/> string stays the persisted source of truth;
    /// this collection is a view over it that is recomposed on every edit.</summary>
    public ObservableCollection<CommandSegment> Commands { get; } = [];

    public IRelayCommand ClearCommandCommand => field ??= Relay.Create(ClearCommandOnly);

    /// <summary>True when the button has a non-empty command assigned.</summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(ButtonData?.Command);

    public SimpleButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        ICommandRegistry commandRegistry)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;

        // Keep the 1-based sequence numbers on the chips in sync with the
        // collection (insert, remove, move, clear, initial load).
        Commands.CollectionChanged += (_, _) => RenumberSegments();

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.SimpleButton);
    }

    /// <summary>Parses <see cref="LoupedeckButton.Command"/> into editable segment cards.
    /// Does not write back — opening (and closing without edits) leaves the persisted
    /// string byte-for-byte unchanged.</summary>
    private void LoadSegments()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        if (ButtonData == null) return;

        foreach (var raw in CommandStringParser.SplitChain(ButtonData.Command))
            Commands.Add(CreateSegment(raw));
    }

    /// <summary>Builds a <see cref="CommandSegment"/> from a raw segment, resolving its
    /// <see cref="Commands.Base.CommandInfo"/> when the command name is a known system command.</summary>
    private CommandSegment CreateSegment(string raw)
    {
        var name = CommandStringParser.GetName(raw);
        var info = _commandRegistry.Get(name)?.Info;
        var segment = CommandSegment.Create(_commandBuilder, info, raw);
        segment.Changed += OnSegmentChanged;
        return segment;
    }

    private void OnSegmentChanged(object sender, EventArgs e) => RebuildCommandString();

    /// <summary>Reassigns the 1-based <see cref="CommandSegment.Position"/> shown
    /// on every chip in the sequence strip.</summary>
    private void RenumberSegments()
    {
        for (var i = 0; i < Commands.Count; i++)
            Commands[i].Position = i + 1;
    }

    /// <summary>Recomposes the persisted <c>&amp;&amp;</c>-joined command string from the
    /// current card order/values. Called after every add / remove / reorder / edit.</summary>
    private void RebuildCommandString()
    {
        if (ButtonData == null) return;

        var joined = string.Join(" && ",
            Commands.Select(s => s.Raw).Where(r => !string.IsNullOrWhiteSpace(r)));

        ButtonData.Command = string.IsNullOrWhiteSpace(joined) ? null : joined;
        NotifyCommandChanged();
    }

    /// <summary>Appends a command (double-click in the tree) to the end of the chain.</summary>
    public void InsertCommand(MenuEntry menuEntry) => InsertCommandAt(menuEntry, Commands.Count);

    /// <summary>Inserts a command (drag from the tree) at the given card index.</summary>
    public void InsertCommandAt(MenuEntry menuEntry, int index)
    {
        if (ButtonData == null || menuEntry == null) return;

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

    /// <summary>Clears all assigned commands — leaves the button color untouched.</summary>
    public void ClearCommandOnly()
    {
        if (ButtonData == null) return;
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();
        ButtonData.Command = null;
        NotifyCommandChanged();
    }

    private void NotifyCommandChanged()
    {
        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Detaches segment handlers — called by the View when the dialog closes.</summary>
    public void Cleanup()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
    }
}
