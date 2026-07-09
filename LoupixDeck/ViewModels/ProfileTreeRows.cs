using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Editor row for one <see cref="Models.Workspace"/> in the Profiles settings tree (issue #132).
/// Wraps the model for inline rename and exposes live "home / active" badges refreshed via
/// <see cref="RefreshFlags"/> when the active profile/workspace changes.
/// </summary>
public partial class WorkspaceRow(Workspace workspace, ProfileRow parent) : ObservableObject
{
    public Workspace Workspace { get; } = workspace;
    public ProfileRow Parent { get; } = parent;

    public string Name
    {
        get => Workspace.Name;
        set
        {
            if (Workspace.Name == value) return;
            Workspace.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Name with a numbered fallback so an unnamed workspace still reads sensibly.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Workspace.Name)
            ? $"Workspace {Parent.Workspaces.IndexOf(this) + 1}"
            : Workspace.Name;

    public bool IsHome => Parent.Profile.HomeWorkspaceId == Workspace.Id;

    public bool IsActiveWorkspace =>
        Parent.IsActiveProfile && Parent.Owner.Config.ActiveWorkspaceId == Workspace.Id;

    public void RefreshFlags()
    {
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsActiveWorkspace));
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>
/// Editor row for one <see cref="Models.Profile"/> in the Profiles settings tree (issue #132).
/// Holds the profile's <see cref="WorkspaceRow"/>s and exposes live "active / startup" badges.
/// </summary>
public partial class ProfileRow : ObservableObject
{
    public Profile Profile { get; }
    public SettingsViewModel Owner { get; }
    public ObservableCollection<WorkspaceRow> Workspaces { get; } = new();

    public ProfileRow(Profile profile, SettingsViewModel owner)
    {
        Profile = profile;
        Owner = owner;
        foreach (var workspace in profile.Workspaces)
            Workspaces.Add(new WorkspaceRow(workspace, this));
    }

    public string Name
    {
        get => Profile.Name;
        set
        {
            if (Profile.Name == value) return;
            Profile.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Profile.Name)
            ? $"Profile {Owner.ProfileRows.IndexOf(this) + 1}"
            : Profile.Name;

    public bool IsActiveProfile => Owner.Config.ActiveProfileId == Profile.Id;
    public bool IsStartupProfile => Owner.Config.StartupProfileId == Profile.Id;

    public void RefreshFlags()
    {
        OnPropertyChanged(nameof(IsActiveProfile));
        OnPropertyChanged(nameof(IsStartupProfile));
        OnPropertyChanged(nameof(DisplayName));
        foreach (var workspace in Workspaces)
            workspace.RefreshFlags();
    }
}
