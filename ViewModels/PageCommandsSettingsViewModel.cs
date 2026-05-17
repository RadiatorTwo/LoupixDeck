using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels.Base;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Edits the per-page command wraps (Pre/Post Execution Commands chained
/// around every button command). Accepts either a <see cref="RotaryButtonPage"/>
/// (4 slots) or <see cref="TouchButtonPage"/> (1 slot) via Initialize.
/// </summary>
public class PageCommandsSettingsViewModel : DialogViewModelBase<object, DialogResult>, IAsyncInitViewModel
{
    private readonly IObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    private readonly ISysCommandService _sysCommandService;
    private readonly ICommandBuilder _commandBuilder;
    private MenuEntry _elgatoKeyLightMenu;

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action CloseRequested;

    public ObservableCollection<WrapSlot> Slots { get; } = new();
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; } = new();
    public MenuEntry CurrentMenuEntry { get; set; }

    public string PageName { get; private set; }

    /// <summary>The TextBox the user clicked into last — defines where InsertCommand appends.</summary>
    private WrapSlot _activeSlot;
    private bool _activeIsPost;

    public PageCommandsSettingsViewModel(IObsController obs, ElgatoDevices elgatoDevices,
        ISysCommandService sysCommandService, ICommandBuilder commandBuilder)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        _sysCommandService = sysCommandService;
        _commandBuilder = commandBuilder;

        ConfirmCommand = new RelayCommand(ConfirmDialog);
        CancelCommand = new RelayCommand(CancelDialog);
    }

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

    public Task InitializeAsync() => CreateSystemMenu();

    public void SetActiveTarget(WrapSlot slot, bool isPost)
    {
        _activeSlot = slot;
        _activeIsPost = isPost;
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        if (_activeSlot == null) return;
        var formatted = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrEmpty(formatted)) return;

        if (_activeIsPost)
            _activeSlot.Wrap.PostCommands = Utils.CommandChain.Append(_activeSlot.Wrap.PostCommands, formatted);
        else
            _activeSlot.Wrap.PreCommands = Utils.CommandChain.Append(_activeSlot.Wrap.PreCommands, formatted);
    }

    private async Task CreateSystemMenu()
    {
        CreatePagesMenu();
        CreateDeviceControlMenu();
        var obsTask = CreateObsMenu();
        CreateElgatoMenu();
        await obsTask;
    }

    private void CreateDeviceControlMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Device Control");
        var groupMenu = new MenuEntry("Device Control", string.Empty);
        foreach (var command in commands)
        {
            if (command.CommandName == "System.DeviceWakeup") continue;
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }
        SystemCommandMenus.Add(groupMenu);
    }

    private void CreatePagesMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Pages");
        var groupMenu = new MenuEntry("Pages", string.Empty);
        foreach (var command in commands)
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        SystemCommandMenus.Add(groupMenu);
    }

    private async Task CreateObsMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "OBS");
        var groupMenu = new MenuEntry("OBS", string.Empty);
        foreach (var command in commands)
        {
            if (command.CommandName == "System.ObsSetScene") continue;
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var scenesMenu = new MenuEntry("Scenes", string.Empty);
        groupMenu.Children.Add(scenesMenu);
        SystemCommandMenus.Add(groupMenu);

        try
        {
            var scenes = await _obs.GetScenes();
            foreach (var scene in scenes)
                scenesMenu.Children.Add(new MenuEntry(scene.Name, $"System.ObsSetScene({scene.Name})"));
        }
        catch (Exception ex)
        {
            scenesMenu.Children.Add(new MenuEntry($"OBS not connected: {ex.Message}", string.Empty));
        }
    }

    private void CreateElgatoMenu()
    {
        _elgatoKeyLightMenu = new MenuEntry("Elgato Keylights", string.Empty);
        foreach (var keyLight in _elgatoDevices.KeyLights) AddKeyLightMenuEntry(keyLight);
        _elgatoDevices.KeyLightAdded += (_, e) => AddKeyLightMenuEntry(e);
        SystemCommandMenus.Add(_elgatoKeyLightMenu);
    }

    private void AddKeyLightMenuEntry(KeyLight keyLight)
    {
        if (_elgatoKeyLightMenu.Children.Any(kl => kl.Name == keyLight.DisplayName)) return;
        var keyLightGroup = new MenuEntry(keyLight.DisplayName, null);
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Elgato Keylights");
        foreach (var command in commands)
            keyLightGroup.Children.Add(new MenuEntry(command.DisplayName, command.CommandName, keyLight.DisplayName));
        _elgatoKeyLightMenu.Children.Add(keyLightGroup);
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
public class WrapSlot
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
