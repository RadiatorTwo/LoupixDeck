using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Editor row wrapper for a single <see cref="ContextRule"/> (issue #132). Maps the rule's
/// nullable profile/workspace ids to <see cref="Profile"/>/<see cref="Workspace"/> ComboBox
/// selections and keeps the workspace option list in sync with the chosen profile.
/// </summary>
public partial class ContextRuleRow : ObservableObject
{
    public ContextRule Rule { get; }

    /// <summary>All profiles (shared instance) — the target-profile ComboBox's ItemsSource.</summary>
    public ObservableCollection<Profile> Profiles { get; }

    /// <summary>Workspaces of the selected profile — the target-workspace ComboBox's ItemsSource.</summary>
    public ObservableCollection<Workspace> Workspaces { get; } = new();

    public ContextRuleRow(ContextRule rule, ObservableCollection<Profile> profiles)
    {
        Rule = rule;
        Profiles = profiles;
        RebuildWorkspaces();
    }

    /// <summary>Target profile (null = leave the active profile unchanged).</summary>
    public Profile SelectedProfile
    {
        get => Profiles.FirstOrDefault(p => p.Id == Rule.ActivateProfileId);
        set
        {
            Rule.ActivateProfileId = value?.Id;
            OnPropertyChanged();
            RebuildWorkspaces();
            OnPropertyChanged(nameof(SelectedWorkspace));
        }
    }

    /// <summary>Target workspace within the selected profile (null = the profile's home workspace).</summary>
    public Workspace SelectedWorkspace
    {
        get => Workspaces.FirstOrDefault(w => w.Id == Rule.ActivateWorkspaceId);
        set
        {
            Rule.ActivateWorkspaceId = value?.Id;
            OnPropertyChanged();
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

    private void RebuildWorkspaces()
    {
        Workspaces.Clear();
        var profile = Profiles.FirstOrDefault(p => p.Id == Rule.ActivateProfileId);
        if (profile != null)
            foreach (var workspace in profile.Workspaces)
                Workspaces.Add(workspace);

        // Drop a workspace target that no longer belongs to the chosen profile.
        if (Rule.ActivateWorkspaceId is { } wid && Workspaces.All(w => w.Id != wid))
            Rule.ActivateWorkspaceId = null;
    }
}
