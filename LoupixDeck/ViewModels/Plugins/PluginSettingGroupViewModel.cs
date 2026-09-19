using System.Collections.ObjectModel;
using LoupixDeck.Localization;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// A card in the detail pane. A plugin splits its form by declaring
/// <see cref="PluginSdk.PluginSettingKind.Heading"/> descriptors; each heading opens a group,
/// and the settings that follow belong to it. Settings declared before the first heading go
/// into an untitled group so a plugin that declares none still renders.
/// </summary>
public sealed class PluginSettingGroupViewModel(string title, string description, string pluginId) : ViewModelBase
{
    public string Title => LocalizationManager.Instance.TrText(title, pluginId);

    public string Description => LocalizationManager.Instance.TrText(description, pluginId);

    public bool HasTitle => !string.IsNullOrWhiteSpace(title);

    public bool HasDescription => !string.IsNullOrWhiteSpace(description);

    public ObservableCollection<PluginSettingRowViewModel> Rows { get; } = [];

    /// <summary>Re-reads the heading and every row's texts after the UI language changed.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        foreach (PluginSettingRowViewModel row in Rows)
            row.RefreshTexts();
    }
}
