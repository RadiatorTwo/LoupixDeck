using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models.Companion;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Settings → Companions: create, rename and dissolve companion groups, choose each group's master
/// and add or remove companions, with every device's connection state. All rule checks happen in the
/// coordinator; this view only offers devices that can be chosen and shows the coordinator's reason
/// when an assignment is refused. Groups are global, so the page looks the same from every device.
/// </summary>
public sealed partial class CompanionGroupsViewModel : ObservableObject, IDisposable
{
    private readonly ICompanionCoordinator _coordinator;

    public CompanionGroupsViewModel(ICompanionCoordinator coordinator)
    {
        _coordinator = coordinator;
        _coordinator.GroupsChanged += OnCoordinatorChanged;
        _coordinator.DeviceOnlineStateChanged += OnDeviceOnlineStateChanged;
        Refresh();
    }

    public ObservableCollection<CompanionGroupRow> Groups { get; } = new();

    /// <summary>Known devices that cannot join a group because they report no serial number.</summary>
    public ObservableCollection<string> DevicesWithoutSerial { get; } = new();

    public bool HasDevicesWithoutSerial => DevicesWithoutSerial.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    [RelayCommand]
    private void AddGroup() => _coordinator.CreateGroup(Loc.Tr("Companions_NewGroupName"));

    internal ICompanionCoordinator Coordinator => _coordinator;

    /// <summary>Rebuilds every row from the coordinator.</summary>
    public void Refresh()
    {
        IReadOnlyList<CompanionDeviceInfo> devices = _coordinator.GetKnownDevices();

        // Keep the error text of a row across a refresh triggered by its own refused edit.
        Dictionary<Guid, string> errors = Groups.Where(g => !string.IsNullOrEmpty(g.Error))
            .ToDictionary(g => g.Group.Id, g => g.Error);

        Groups.Clear();
        foreach (CompanionGroup group in _coordinator.Groups)
        {
            CompanionGroupRow row = new(this, group, devices);
            if (errors.TryGetValue(group.Id, out string error)) row.Error = error;
            Groups.Add(row);
        }

        DevicesWithoutSerial.Clear();
        foreach (CompanionDeviceInfo device in devices.Where(d => !_coordinator.CanJoinGroup(d.Key)))
            DevicesWithoutSerial.Add(device.DisplayName);

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasDevicesWithoutSerial));
    }

    private void OnCoordinatorChanged() => Dispatcher.UIThread.Post(Refresh);

    private void OnDeviceOnlineStateChanged(string _) => Dispatcher.UIThread.Post(Refresh);

    public void Dispose()
    {
        _coordinator.GroupsChanged -= OnCoordinatorChanged;
        _coordinator.DeviceOnlineStateChanged -= OnDeviceOnlineStateChanged;
    }
}

/// <summary>One device choice in a group editor.</summary>
public sealed record CompanionDeviceOption(string Key, string DisplayName, bool IsOnline)
{
    public string StatusText => Loc.Tr(IsOnline ? "Companion_Connected" : "Companion_Offline");
}

/// <summary>One entry of a group's page follow dropdown. Carries the translation key and exposes the live
/// <see cref="TranslatedString"/>, so the open dropdown follows a language change.</summary>
public sealed record CompanionPageFollowOption(CompanionPageFollowMode Value, string LabelKey)
{
    public TranslatedString Label => LocalizationManager.Instance.Entry(LabelKey);
}

/// <summary>One companion group in the editor.</summary>
public sealed partial class CompanionGroupRow : ObservableObject
{
    private readonly CompanionGroupsViewModel _owner;

    public CompanionGroupRow(CompanionGroupsViewModel owner, CompanionGroup group, IReadOnlyList<CompanionDeviceInfo> devices)
    {
        _owner = owner;
        Group = group;

        Dictionary<string, CompanionDeviceInfo> byKey = devices.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        CompanionDeviceOption Option(string key) => byKey.TryGetValue(key, out CompanionDeviceInfo info)
            ? new CompanionDeviceOption(key, info.DisplayName, info.IsOnline)
            : new CompanionDeviceOption(key, key, false);

        HashSet<string> members = devices
            .Select(d => d.Key)
            .Where(key => owner.Coordinator.FindGroup(key) != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(group.MasterDeviceKey))
            Master = Option(group.MasterDeviceKey);

        foreach (string key in group.CompanionDeviceKeys)
            Companions.Add(Option(key));

        // Only devices in no group (drafts included) that can be told apart can be chosen; the current
        // master stays in the list so the selection shows it.
        foreach (CompanionDeviceInfo device in devices.Where(d => owner.Coordinator.CanJoinGroup(d.Key)))
        {
            bool free = !members.Contains(device.Key);
            if (free || string.Equals(device.Key, group.MasterDeviceKey, StringComparison.OrdinalIgnoreCase))
                MasterOptions.Add(Option(device.Key));
            if (free)
                CompanionCandidates.Add(Option(device.Key));
        }
    }

    public CompanionGroup Group { get; }

    public string Name
    {
        get => Group.Name;
        set
        {
            if (string.Equals(value, Group.Name, StringComparison.Ordinal)) return;
            _owner.Coordinator.RenameGroup(Group.Id, value);
            OnPropertyChanged();
        }
    }

    public CompanionDeviceOption Master { get; }

    public ObservableCollection<CompanionDeviceOption> MasterOptions { get; } = new();

    public CompanionDeviceOption SelectedMaster
    {
        get => MasterOptions.FirstOrDefault(o => string.Equals(o.Key, Group.MasterDeviceKey, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value == null) return;
            Error = _owner.Coordinator.TrySetMaster(Group.Id, value.Key, out string error) ? null : error;
            // A refused choice must not stay selected in the ComboBox.
            OnPropertyChanged();
        }
    }

    public ObservableCollection<CompanionDeviceOption> Companions { get; } = new();

    /// <summary>The page follow modes, in the order they are offered.</summary>
    public IReadOnlyList<CompanionPageFollowOption> PageFollowOptions { get; } =
    [
        new(CompanionPageFollowMode.Off, "Companions_PageFollowOff"),
        new(CompanionPageFollowMode.TouchPages, "Companions_PageFollowTouch"),
        new(CompanionPageFollowMode.TouchAndRotaryPages, "Companions_PageFollowTouchAndRotary")
    ];

    public CompanionPageFollowOption SelectedPageFollow
    {
        get => PageFollowOptions.FirstOrDefault(o => o.Value == Group.PageFollow) ?? PageFollowOptions[0];
        set
        {
            if (value == null || value.Value == Group.PageFollow) return;
            _owner.Coordinator.SetPageFollow(Group.Id, value.Value);
            OnPropertyChanged();
        }
    }

    public ObservableCollection<CompanionDeviceOption> CompanionCandidates { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCompanionCommand))]
    public partial CompanionDeviceOption SelectedCandidate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>True while the group lacks a master or a companion; it then assigns no roles.</summary>
    public bool IsIncomplete => string.IsNullOrWhiteSpace(Group.MasterDeviceKey) || Group.CompanionDeviceKeys.Count == 0;

    [RelayCommand(CanExecute = nameof(CanAddCompanion))]
    private void AddCompanion()
    {
        if (SelectedCandidate == null) return;
        Error = _owner.Coordinator.TryAddCompanion(Group.Id, SelectedCandidate.Key, out string error) ? null : error;
    }

    private bool CanAddCompanion() => SelectedCandidate != null;

    [RelayCommand]
    private void RemoveCompanion(CompanionDeviceOption companion)
    {
        if (companion != null)
            _owner.Coordinator.RemoveCompanion(Group.Id, companion.Key);
    }

    [RelayCommand]
    private void RemoveGroup() => _owner.Coordinator.RemoveGroup(Group.Id);
}
