using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Portable;
using LoupixDeck.Services.StarterProfiles.Templates;

namespace LoupixDeck.Services.StarterProfiles;

/// <summary>What creating a starter profile did.</summary>
/// <param name="Profile">The new profile, already active.</param>
/// <param name="MissingPlugins">Plugins the profile uses that are not installed.</param>
/// <param name="EnabledPlugins">Installed plugins that were switched on for this device.</param>
public sealed record StarterProfileResult(
    Profile Profile,
    IReadOnlyList<StarterPluginRequirement> MissingPlugins,
    IReadOnlyList<string> EnabledPlugins);

/// <summary>
/// Creates the starter profiles shipped with the app (issue #301) on this device. A starter profile
/// is always added next to the existing ones and never replaces anything.
/// </summary>
public interface IStarterProfileService
{
    IReadOnlyList<StarterProfileTemplate> Templates { get; }

    /// <summary>True on Windows; templates build their Linux variant otherwise.</summary>
    bool IsWindows { get; }

    /// <summary>False while the device is not connected: a profile is built for its controls.</summary>
    bool CanCreate { get; }

    /// <summary>
    /// Builds <paramref name="template"/> for this device, adds it as a new profile, enables the
    /// plugins it uses for this device, activates it and saves the config. A plugin that is not
    /// installed yet is enabled ahead, so it works as soon as it is installed.
    /// </summary>
    Task<StarterProfileResult> CreateAsync(StarterProfileTemplate template);

    /// <summary>
    /// Enables the template's plugins that are installed but not enabled for this device, e.g. after
    /// they were installed from the Plugin Store. Returns the names of the plugins it enabled.
    /// </summary>
    Task<IReadOnlyList<string>> EnableInstalledPluginsAsync(StarterProfileTemplate template);

    /// <summary>The template's plugins that are not installed.</summary>
    IReadOnlyList<StarterPluginRequirement> FindMissingPlugins(StarterProfileTemplate template);
}

public sealed class StarterProfileService(
    LoupedeckConfig config,
    IDeviceService deviceService,
    IPageManager pageManager,
    IWorkspaceActivationService activation,
    IDeviceController controller,
    IPluginManager pluginManager,
    IPluginReloadService pluginReload,
    IStarterArt art) : IStarterProfileService
{
    public IReadOnlyList<StarterProfileTemplate> Templates { get; } =
    [
        new BasicStarterTemplate(),
        new FeatureTourTemplate(),
        new MediaTemplate(),
        new ObsTemplate(),
        new DaVinciResolveTemplate(),
        new HomeAssistantTemplate()
    ];

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool CanCreate => deviceService.TouchButtonCount > 0;

    public async Task<StarterProfileResult> CreateAsync(StarterProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        // Enabled first, so the plugin's commands resolve when the profile is activated below.
        IReadOnlyList<string> enabled = await EnableInstalledPluginsAsync(template);

        // Installing a plugin never enables it for any device, and a store install that needs a
        // restart only loads on the next start. Without its id in this device's list the profile
        // would then stay blank, so the id is put there now; the plugin loads enabled once it is
        // installed, live or after the restart.
        config.EnabledPlugins ??= [];
        foreach (StarterPluginRequirement plugin in FindMissingPlugins(template).Where(plugin => !IsEnabledHere(plugin)))
            config.EnabledPlugins.Add(plugin.Id);

        DeviceShape shape = new(deviceService.TouchButtonCount, deviceService.RotaryButtonCount,
            pageManager.SideRotaryButtonCount, pageManager.HasIndependentRotarySides, config.Geometry);

        Profile profile = new() { Name = UniqueName(template.Name) };
        template.Build(new StarterProfileBuilder(shape, IsWindows, art, template.Id), profile);
        PortablePayloadNormalizer.Normalize(profile, shape.TouchButtonCount, shape.RotaryButtonCount,
            shape.SideRotaryButtonCount);

        // Saved straight away, before anything is awaited: the wallpapers and animations are already
        // in the asset store, and another device's save sweeps assets no config on disk references.
        config.Profiles.Add(profile);
        controller.SaveConfig();

        await activation.ActivateProfile(profile.Id);
        controller.SaveConfig();

        return new StarterProfileResult(profile, FindMissingPlugins(template), enabled);
    }

    public async Task<IReadOnlyList<string>> EnableInstalledPluginsAsync(StarterProfileTemplate template)
    {
        List<string> enabled = [];

        foreach (StarterPluginRequirement plugin in template.GetRequiredPlugins(IsWindows))
        {
            if (!IsInstalled(plugin) || IsEnabledHere(plugin))
                continue;

            PluginActionResult result = await pluginReload.EnableAsync(plugin.Id);
            if (result.Success)
                enabled.Add(plugin.Name);
            else
                Console.WriteLine($"[StarterProfiles] Could not enable '{plugin.Id}': {result.Message}");
        }

        return enabled;
    }

    public IReadOnlyList<StarterPluginRequirement> FindMissingPlugins(StarterProfileTemplate template) =>
        template.GetRequiredPlugins(IsWindows).Where(plugin => !IsInstalled(plugin)).ToList();

    private bool IsInstalled(StarterPluginRequirement plugin) =>
        pluginManager.Plugins.Any(p => string.Equals(p.Manifest?.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));

    private bool IsEnabledHere(StarterPluginRequirement plugin) =>
        config.EnabledPlugins?.Any(id => string.Equals(id, plugin.Id, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>The template's name, numbered when a profile of that name already exists.</summary>
    private string UniqueName(string name)
    {
        string candidate = name;
        for (int number = 2; config.Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = $"{name} {number}";

        return candidate;
    }
}