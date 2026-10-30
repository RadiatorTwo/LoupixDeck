using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.Portable;
using LoupixDeck.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Offers to enable the plugins pasted content needs (issue #535). Plugins are loaded once for the
/// whole process but enabled per device, so a key copied on a device that uses a plugin can land on
/// one where that plugin is off and its commands do nothing. The plugin is already installed, so a
/// single question is enough; nothing is installed here.
/// </summary>
public interface IPastePluginActivation
{
    /// <summary>
    /// Finds the installed plugins whose commands appear in <paramref name="snapshots"/> and that are
    /// not enabled on this device, asks once and enables them on yes. Returns without asking when
    /// there are none. Declining does not stop the paste.
    /// </summary>
    Task EnableRequiredAsync(IEnumerable<string> snapshots);
}

public sealed class PastePluginActivation(
    LoupedeckConfig config,
    IPluginManager pluginManager,
    IPluginReloadService pluginReload,
    IDialogService dialogService) : IPastePluginActivation
{
    public async Task EnableRequiredAsync(IEnumerable<string> snapshots)
    {
        List<LoadedPlugin> missing = FindDisabledPlugins(snapshots);
        if (missing.Count == 0) return;

        string names = string.Join(", ", missing.Select(static p => p.Manifest.Name ?? p.Manifest.Id));
        string message = missing.Count == 1
            ? Loc.Tr("Confirm_EnablePastePluginMessage", names)
            : Loc.Tr("Confirm_EnablePastePluginsMessage", names);

        DialogResult result = await dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
            vm.Configure(
                message,
                title: Loc.Tr("Confirm_EnablePastePluginTitle"),
                confirmText: Loc.Tr("Confirm_Yes"),
                cancelText: Loc.Tr("Confirm_No")));
        if (result is not { IsConfirmed: true }) return;

        foreach (LoadedPlugin plugin in missing)
        {
            PluginActionResult enabled = await pluginReload.EnableAsync(plugin.Manifest.Id);
            if (!enabled.Success)
                Console.WriteLine($"[Paste] Could not enable plugin '{plugin.Manifest.Id}': {enabled.Message}");
        }
    }

    private List<LoadedPlugin> FindDisabledPlugins(IEnumerable<string> snapshots)
    {
        HashSet<string> commandNames = new(StringComparer.Ordinal);
        foreach (string snapshot in snapshots ?? [])
        {
            if (string.IsNullOrEmpty(snapshot)) continue;

            try
            {
                commandNames.UnionWith(PortableCommandScanner.CollectCommandNames(JToken.Parse(snapshot)));
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Paste] Could not scan the clipboard for plugin commands: {ex.Message}");
            }
        }

        if (commandNames.Count == 0) return [];

        return [.. pluginManager.Plugins
            .Where(p => p?.Manifest?.Id != null && !IsEnabledHere(p.Manifest.Id))
            .Where(p => p.Commands.Any(c => c?.Descriptor?.CommandName != null && commandNames.Contains(c.Descriptor.CommandName)))];
    }

    private bool IsEnabledHere(string pluginId)
        => config.EnabledPlugins?.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) == true;
}
