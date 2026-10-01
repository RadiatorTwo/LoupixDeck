using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Services.StarterProfiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.Plugins;

namespace LoupixDeck.ViewModels;

/// <summary>One template in the starter profile picker.</summary>
public sealed class StarterTemplateRow(StarterProfileTemplate template, bool isWindows)
{
    public StarterProfileTemplate Template { get; } = template;
    public string Name => Template.Name;
    public string Description => Template.Description;
    public string Glyph => Template.Glyph;

    public string PluginsText { get; } = template.RequiredPluginsText(isWindows) is { Length: > 0 } plugins
        ? Loc.Tr("StarterPicker_NeedsPlugin", plugins)
        : string.Empty;

    public bool HasPlugins => PluginsText.Length > 0;
}

/// <summary>
/// Lets the user pick a starter profile (issue #301) and creates it on this device. Creating never
/// asks anything about the profile itself: it is always added as a new one. Only a missing plugin
/// leads to a question, whether to open the Plugin Store for it.
/// </summary>
public sealed partial class StarterProfilePickerViewModel : DialogViewModelBase<DialogResult>
{
    private readonly IStarterProfileService _starter;
    private readonly IPluginStoreService _pluginStore;
    private readonly ResolvedDevice _device;

    public StarterProfilePickerViewModel(IStarterProfileService starter, IPluginStoreService pluginStore,
        ResolvedDevice device)
    {
        _starter = starter;
        _pluginStore = pluginStore;
        _device = device;
        Templates = starter.Templates.Select(t => new StarterTemplateRow(t, starter.IsWindows)).ToList();
        SelectedTemplate = Templates.FirstOrDefault();
    }

    /// <summary>
    /// Shows the picker, creates the chosen profile and offers to install the plugins it uses.
    /// Returns a summary of what happened, or null when the user cancelled. Shared by every place
    /// that offers starter profiles.
    /// </summary>
    public static async Task<string> ShowAsync(IDialogService dialogService)
    {
        StarterProfilePickerViewModel picker = null;
        DialogResult result = await dialogService.ShowDialogAsync<StarterProfilePickerViewModel, DialogResult>(
            vm => picker = vm);

        if (result?.IsConfirmed != true || picker?.SelectedTemplate == null)
            return null;

        return await picker.CreateAsync(dialogService);
    }

    public event Action CloseWindow;

    public IReadOnlyList<StarterTemplateRow> Templates { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial StarterTemplateRow SelectedTemplate { get; set; }

    /// <summary>A profile is built for the device's controls, so it cannot be built while it is offline.</summary>
    public bool IsDeviceUnavailable => !_starter.CanCreate;

    public IRelayCommand CreateCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    }, () => SelectedTemplate != null && _starter.CanCreate);

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    private async Task<string> CreateAsync(IDialogService dialogService)
    {
        StarterProfileTemplate template = SelectedTemplate.Template;

        // A plugin whose removal finishes on the next start cannot be installed again before that:
        // its store row only offers the restart. Creating the profile now would leave it without its
        // plugin, so it is not created until LoupixDeck has restarted.
        List<StarterPluginRequirement> beingRemoved = _starter.FindMissingPlugins(template)
            .Where(p => _pluginStore.IsRestartRequired(p.Id) && !_pluginStore.IsPresentAfterRestart(p.Id))
            .ToList();
        if (beingRemoved.Count > 0)
            return Loc.Tr("StarterPicker_RestartFirst", Names(beingRemoved.Select(p => p.Name)), template.Name);

        StarterProfileResult result;

        try
        {
            result = await _starter.CreateAsync(template);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StarterProfiles] Creating '{template.Id}' failed: {ex}");
            return Loc.Tr("StarterPicker_Failed", ex.Message);
        }

        List<string> enabled = [.. result.EnabledPlugins];
        IReadOnlyList<StarterPluginRequirement> missing = result.MissingPlugins;

        // A plugin that waits for a restart (installed, updated or removed in this session) cannot
        // be installed from the store until then: its row only offers the restart. Offering the
        // store for it would lead nowhere, so only the others are offered.
        List<StarterPluginRequirement> installable = missing.Where(p => !_pluginStore.IsRestartRequired(p.Id)).ToList();
        if (installable.Count > 0 && await AskToInstallAsync(installable))
        {
            await dialogService.ShowDialogAsync<PluginsWindowViewModel, DialogResult>(vm =>
            {
                vm.Installed.SelectDevice(_device.ScopeKey);
                vm.OpenPluginStore(installable[0].Id);
            });

            // Switches on what was installed and loaded live; a plugin is already enabled ahead
            // for this device, so this only matters where that had no effect yet.
            enabled.AddRange(await _starter.EnableInstalledPluginsAsync(template));
            missing = _starter.FindMissingPlugins(template);
        }

        // Every missing plugin is already enabled for this device, so what is left to say is what
        // the user has to do: restart (an install or update finishes then) or install.
        List<StarterPluginRequirement> afterRestart = missing.Where(p => _pluginStore.IsRestartRequired(p.Id)).ToList();
        List<StarterPluginRequirement> notInstalled = missing.Except(afterRestart).ToList();

        List<string> lines = [Loc.Tr("StarterPicker_Created", result.Profile.Name)];
        if (enabled.Count > 0)
            lines.Add(Loc.Tr("StarterPicker_PluginsEnabled", Names(enabled)));
        if (afterRestart.Count > 0)
            lines.Add(Loc.Tr("StarterPicker_PluginsAfterRestart", Names(afterRestart.Select(p => p.Name))));
        if (notInstalled.Count > 0)
            lines.Add(Loc.Tr("StarterPicker_PluginsStillMissing", Names(notInstalled.Select(p => p.Name))));
        if (template.SetupNote is { Length: > 0 } note)
            lines.Add(note);

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);

    private static Task<bool> AskToInstallAsync(IReadOnlyList<StarterPluginRequirement> missing) =>
        ConfirmDialogHelper.AskYesNoAsync(WindowHelper.GetActiveWindow(),
            Loc.Tr("StarterPicker_PluginMissingTitle"),
            Loc.Tr("StarterPicker_PluginMissingMessage", string.Join(", ", missing.Select(p => p.Name))));
}