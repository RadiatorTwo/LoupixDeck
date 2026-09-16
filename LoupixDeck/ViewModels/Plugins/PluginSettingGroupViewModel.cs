using System.Collections.ObjectModel;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// A card in the detail pane. A plugin splits its form by declaring
/// <see cref="PluginSdk.PluginSettingKind.Heading"/> descriptors; each heading opens a group,
/// and the settings that follow belong to it. Settings declared before the first heading go
/// into an untitled group so a plugin that declares none still renders.
/// </summary>
public sealed class PluginSettingGroupViewModel(string title, string description) : ViewModelBase
{
    public string Title { get; } = title;

    public string Description { get; } = description;

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public ObservableCollection<PluginSettingRowViewModel> Rows { get; } = [];
}
