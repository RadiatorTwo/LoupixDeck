using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// The card that carries a plugin's action buttons and whatever they report back. A
/// <see cref="PluginSettingAction"/> answers with a short string ("Connected to …",
/// "Could not reach …"), which used to be dropped under the last field where it was easy to
/// miss; it now has a surface of its own. Omitted entirely when a plugin declares no actions.
/// </summary>
public sealed partial class PluginConnectionViewModel : ViewModelBase
{
    public ObservableCollection<PluginActionRowViewModel> Actions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string ResultText { get; set; }

    /// <summary>
    /// Whether the action itself blew up. It is deliberately not inferred from the message: a
    /// PluginSettingAction answers with a free-form string, so a plugin reporting "could not
    /// reach the server" is, to the host, a call that completed. Claiming success for such a
    /// message would be worse than saying nothing, so only a thrown exception is marked, and
    /// everything else is shown as the neutral report it is.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFailure { get; set; }

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    /// <summary>Re-reads the button labels after the UI language changed.</summary>
    public void RefreshTexts()
    {
        foreach (PluginActionRowViewModel action in Actions)
            action.RefreshTexts();
    }
}

/// <summary>One action button. It disables itself for the duration of the call, because a
/// "Test connection" that can be pressed again while it is still running reads as broken.</summary>
public sealed partial class PluginActionRowViewModel(PluginSettingAction action,
    PluginConnectionViewModel card,
    string pluginId,
    Func<Task> beforeInvoke,
    Func<Task> afterInvoke) : ViewModelBase
{
    public string Label => LocalizationManager.Instance.TrText(action.Label, pluginId);

    public void RefreshTexts() => OnPropertyChanged(nameof(Label));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsRunning { get; set; }

    public bool IsIdle => !IsRunning;

    public IAsyncRelayCommand RunCommand => field ??= Relay.Create(RunAsync, () => !IsRunning);

    private async Task RunAsync()
    {
        IsRunning = true;
        RunCommand.NotifyCanExecuteChanged();
        try
        {
            // The action runs against the values on disk, so pending edits are written first -
            // the same order the form has always used.
            await beforeInvoke();

            string result;
            bool failed = false;
            try
            {
                result = await action.Invoke();
            }
            catch (Exception ex)
            {
                result = Loc.Tr("Plugins_ActionFailed", ex.Message);
                failed = true;
            }

            card.ResultText = result;
            card.IsFailure = failed;
        }
        finally
        {
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();
        }

        // A plugin may swap its schema or its actions as a result (an OAuth "Connect" becomes
        // "Disconnect"), so the form is rebuilt from what it declares now.
        await afterInvoke();
    }
}
