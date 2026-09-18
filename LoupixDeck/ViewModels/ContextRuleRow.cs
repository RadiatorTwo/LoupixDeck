using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Editor row wrapper for a single <see cref="ContextRule"/> (issue #132). Maps the rule's
/// nullable profile/workspace ids to <see cref="Profile"/>/<see cref="Workspace"/> ComboBox
/// selections and keeps the workspace option list in sync with the chosen profile. On a master it
/// also lists the device's companions with the pages the rule opens on them.
/// </summary>
public partial class ContextRuleRow : ObservableObject
{
    private readonly ICompanionCoordinator _companions;
    private readonly string _deviceKey;
    private bool _rebuildingWorkspaces;

    public ContextRule Rule { get; }

    /// <summary>All profiles (shared instance) — the target-profile ComboBox's ItemsSource.</summary>
    public ObservableCollection<Profile> Profiles { get; }

    /// <summary>Workspaces of the selected profile — the target-workspace ComboBox's ItemsSource.</summary>
    public ObservableCollection<Workspace> Workspaces { get; } = new();

    /// <summary>One row per companion of this master, plus stale targets of devices that left the group.</summary>
    public ObservableCollection<CompanionRuleTargetRow> CompanionTargets { get; } = new();

    /// <param name="companions">Null where no companion targets are edited.</param>
    /// <param name="deviceKey">Scope key of the device the rule belongs to.</param>
    public ContextRuleRow(ContextRule rule, ObservableCollection<Profile> profiles,
        ICompanionCoordinator companions = null, string deviceKey = null)
    {
        Rule = rule;
        Profiles = profiles;
        _companions = companions;
        _deviceKey = deviceKey;
        RebuildWorkspaces();
        RefreshCompanionTargets();
    }

    /// <summary>Target profile (null = leave the active profile unchanged).</summary>
    public Profile SelectedProfile
    {
        get => Profiles.FirstOrDefault(p => p.Id == Rule.ActivateProfileId);
        set
        {
            // Removing the rule's profile from Profiles makes Avalonia clear the ComboBox's
            // SelectedItem, and the TwoWay binding writes that null back. A package import with
            // "Replace existing" does this too: it removes the old instance and inserts the new one
            // under the same id. Defer the null until that swap is done, so a replaced profile is
            // re-selected instead of dropping the rule's profile and workspace.
            if (value == null && Rule.ActivateProfileId is { } id && Profiles.All(p => p.Id != id))
            {
                Dispatcher.UIThread.Post(() => ResolveRemovedProfile(id));
                return;
            }

            ApplyProfile(value);
        }
    }

    private void ApplyProfile(Profile profile)
    {
        Rule.ActivateProfileId = profile?.Id;
        OnPropertyChanged(nameof(SelectedProfile));
        RebuildWorkspaces();
        OnPropertyChanged(nameof(SelectedWorkspace));
        RefreshCompanionTargets(prune: true);
    }

    /// <summary>Runs after a removal of the rule's profile: re-selects it when a profile with the same
    /// id is back (replaced), otherwise clears the target as before (deleted).</summary>
    private void ResolveRemovedProfile(Guid id)
    {
        if (Rule.ActivateProfileId != id)
            return;

        if (Profiles.All(p => p.Id != id))
        {
            ApplyProfile(null);
            return;
        }

        RebuildWorkspaces();
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedWorkspace));
        RefreshCompanionTargets();
    }

    /// <summary>Target workspace within the selected profile (null = the profile's home workspace).</summary>
    public Workspace SelectedWorkspace
    {
        get => Workspaces.FirstOrDefault(w => w.Id == Rule.ActivateWorkspaceId);
        set
        {
            // Clearing Workspaces in RebuildWorkspaces makes the ComboBox write null here; the
            // rebuild itself decides whether the workspace still belongs to the profile.
            if (_rebuildingWorkspaces)
                return;

            Rule.ActivateWorkspaceId = value?.Id;
            OnPropertyChanged();
            RefreshCompanionTargets(prune: true);
        }
    }

    /// <summary>Priority as text so a plain TextBox can edit it without a binding type converter.
    /// Non-numeric input is ignored (the previous value stays).</summary>
    public string PriorityText
    {
        get => Rule.Priority.ToString();
        set
        {
            if (int.TryParse(value, out var priority))
                Rule.Priority = priority;
            OnPropertyChanged();
        }
    }

    /// <summary>True when the companion section is shown: the device is a master, or the rule still
    /// holds targets from when it was one.</summary>
    public bool ShowCompanionTargets => CompanionTargets.Count > 0;

    /// <summary>True when companions are listed but the rule names neither profile nor workspace, so
    /// there is no workspace to pick their pages from.</summary>
    public bool CompanionTargetsNeedWorkspace => ShowCompanionTargets && TargetWorkspaceId() == null;

    /// <summary>
    /// Rebuilds the companion rows from the current group, connection state and rule target. With
    /// <paramref name="prune"/> (the rule's workspace just changed) page ids that do not exist in the
    /// new workspace are dropped, as a workspace outside the chosen profile is.
    /// </summary>
    public void RefreshCompanionTargets(bool prune = false)
    {
        CompanionTargets.Clear();

        if (_companions != null && !string.IsNullOrEmpty(_deviceKey))
        {
            Guid? workspaceId = TargetWorkspaceId();
            IReadOnlyList<string> companionKeys = _companions.IsMaster(_deviceKey)
                ? _companions.GetCompanionKeys(_deviceKey)
                : [];

            foreach (string companionKey in companionKeys)
            {
                LoupedeckConfig config = _companions.GetDeviceConfig(companionKey);
                Workspace workspace = workspaceId is { } id ? CompanionDeviceTraits.FindWorkspace(config, id) : null;
                CompanionRuleTargetRow row = new(Rule, companionKey, _companions.GetDisplayName(companionKey),
                    _companions.IsOnline(companionKey), isInGroup: true, workspace,
                    CompanionDeviceTraits.HasSideStrips(config), RemoveCompanionTarget)
                {
                    RuleHasWorkspace = workspaceId != null
                };

                // Only prune against a workspace that could be read, or when there is none to pick from.
                if (prune && (workspaceId == null || row.HasWorkspace))
                    row.PruneToWorkspace();

                CompanionTargets.Add(row);
            }

            foreach (CompanionPageTarget target in Rule.CompanionPageTargets?.ToList() ?? [])
            {
                if (companionKeys.Any(key => string.Equals(key, target.DeviceKey, StringComparison.OrdinalIgnoreCase)))
                    continue;

                CompanionTargets.Add(new CompanionRuleTargetRow(Rule, target.DeviceKey,
                    _companions.GetDisplayName(target.DeviceKey), isOnline: false, isInGroup: false,
                    workspace: null, hasSideStrips: false, RemoveCompanionTarget));
            }
        }

        OnPropertyChanged(nameof(ShowCompanionTargets));
        OnPropertyChanged(nameof(CompanionTargetsNeedWorkspace));
    }

    private void RemoveCompanionTarget(CompanionRuleTargetRow row)
    {
        row.RemoveTarget();
        RefreshCompanionTargets();
    }

    /// <summary>The workspace the rule opens: its workspace, else its profile's home workspace.</summary>
    private Guid? TargetWorkspaceId() =>
        Rule.ActivateWorkspaceId ??
        Profiles.FirstOrDefault(p => p.Id == Rule.ActivateProfileId)?.HomeWorkspace?.Id;

    private void RebuildWorkspaces()
    {
        _rebuildingWorkspaces = true;
        try
        {
            Workspaces.Clear();
            var profile = Profiles.FirstOrDefault(p => p.Id == Rule.ActivateProfileId);
            if (profile != null)
                foreach (var workspace in profile.Workspaces)
                    Workspaces.Add(workspace);
        }
        finally
        {
            _rebuildingWorkspaces = false;
        }

        // Drop a workspace target that no longer belongs to the chosen profile.
        if (Rule.ActivateWorkspaceId is { } wid && Workspaces.All(w => w.Id != wid))
            Rule.ActivateWorkspaceId = null;
    }
}
