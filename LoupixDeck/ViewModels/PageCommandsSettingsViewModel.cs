using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Edits the per-page command wraps (Pre/Post Execution Commands chained
/// around every button command). Accepts either a <see cref="RotaryButtonPage"/>
/// (4 slots) or <see cref="TouchButtonPage"/> (1 slot) via Initialize.
/// </summary>
public class PageCommandsSettingsViewModel(ICommandBuilder commandBuilder, IMenuTreeBuilder menuTreeBuilder) : DialogViewModelBase<object, DialogResult>, IAsyncInitViewModel
{
    public IRelayCommand ConfirmCommand => field ??= Relay.Create(ConfirmDialog);
    public IRelayCommand CancelCommand => field ??= Relay.Create(CancelDialog);

    public event Action CloseRequested;

    public ObservableCollection<WrapSlot> Slots { get; } = new();
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; } = new();
    public MenuEntry CurrentMenuEntry { get; set; }

    public string PageName { get; private set; }

    /// <summary>The TextBox the user clicked into last — defines where InsertCommand appends.</summary>
    private WrapSlot _activeSlot;
    private bool _activeIsPost;

    public override void Initialize(object parameter)
    {
        Slots.Clear();
        switch (parameter)
        {
            case RotaryButtonPage rp:
                PageName = rp.PageName;
                Slots.Add(new WrapSlot("Simple Buttons", rp.SimpleButtonWrap));
                Slots.Add(new WrapSlot("Knob — turn left", rp.KnobLeftWrap));
                Slots.Add(new WrapSlot("Knob — turn right", rp.KnobRightWrap));
                Slots.Add(new WrapSlot("Knob — press", rp.KnobPressWrap));
                break;
            case TouchButtonPage tp:
                PageName = tp.PageName;
                Slots.Add(new WrapSlot("Touch Buttons", tp.TouchButtonWrap));
                break;
            default:
                PageName = "Page Commands";
                break;
        }
        // Default insertion target = first Pre field; user can click another to change.
        if (Slots.Count > 0) SetActiveTarget(Slots[0], false);
        OnPropertyChanged(nameof(PageName));
    }

    public async Task InitializeAsync()
    {
        // Page wraps chain around button commands; the SimpleButton target set
        // (Pages, Device Control, OBS, Elgato) is the right scope for them.
        await menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.SimpleButton);
    }

    public void SetActiveTarget(WrapSlot slot, bool isPost)
    {
        _activeSlot = slot;
        _activeIsPost = isPost;
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        if (_activeSlot == null) return;
        var formatted = commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrEmpty(formatted)) return;

        if (_activeIsPost)
            _activeSlot.Wrap.PostCommands = Utils.CommandChain.Append(_activeSlot.Wrap.PostCommands, formatted);
        else
            _activeSlot.Wrap.PreCommands = Utils.CommandChain.Append(_activeSlot.Wrap.PreCommands, formatted);
    }

    private void ConfirmDialog()
    {
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelDialog()
    {
        foreach (var slot in Slots) slot.Revert();
        Cancel();
        CloseRequested?.Invoke();
    }
}

/// <summary>One editable wrap slot with a label and rollback support.</summary>
public sealed class WrapSlot
{
    public string Label { get; }
    public CommandWrap Wrap { get; }

    private readonly bool _origPreEnabled;
    private readonly string _origPreCommands;
    private readonly bool _origPostEnabled;
    private readonly string _origPostCommands;

    public WrapSlot(string label, CommandWrap wrap)
    {
        Label = label;
        Wrap = wrap ?? new CommandWrap();
        _origPreEnabled = Wrap.PreEnabled;
        _origPreCommands = Wrap.PreCommands;
        _origPostEnabled = Wrap.PostEnabled;
        _origPostCommands = Wrap.PostCommands;
    }

    public void Revert()
    {
        Wrap.PreEnabled = _origPreEnabled;
        Wrap.PreCommands = _origPreCommands;
        Wrap.PostEnabled = _origPostEnabled;
        Wrap.PostCommands = _origPostCommands;
    }
}
