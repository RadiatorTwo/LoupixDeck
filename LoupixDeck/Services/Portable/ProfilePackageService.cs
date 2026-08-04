using System.IO.Compression;
using System.Reflection;
using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Models.Macros;
using LoupixDeck.Models.Portable;
using LoupixDeck.Registry;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Macros;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Portable.Migrations;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable;

/// <inheritdoc cref="IProfilePackageService"/>
public sealed class ProfilePackageService(
    IConfigService configService,
    IAssetService assetService,
    IMacroManager macroManager,
    IPluginManager pluginManager,
    ICommandRegistry commandRegistry,
    IDeviceService deviceService,
    IPageManager pageManager,
    DeviceRegistry.DeviceInfo deviceInfo) : IProfilePackageService
{
    /// <summary>
    /// Envelope upgrade chain. Empty at format version 1 — see <see cref="IPackageMigration"/>.
    /// </summary>
    private readonly List<IPackageMigration> _migrations = [];

    public Task<ProfilePackageResult> ExportProfileAsync(Profile profile, string targetPath, string description = null)
    {
        if (profile == null)
            return Task.FromResult(ProfilePackageResult.Fail("No profile selected."));

        return ExportCoreAsync(PackageKind.Profile, new ProfilePackagePayload { Profile = profile },
            profile.Name, targetPath, description);
    }

    public Task<ProfilePackageResult> ExportWorkspaceAsync(Workspace workspace, string targetPath, string description = null)
    {
        if (workspace == null)
            return Task.FromResult(ProfilePackageResult.Fail("No workspace selected."));

        return ExportCoreAsync(PackageKind.Workspace, new ProfilePackagePayload { Workspace = workspace },
            workspace.Name, targetPath, description);
    }

    public Task<ProfilePackageResult> ExportTouchPageAsync(TouchButtonPage page, string targetPath, string description = null)
    {
        if (page == null)
            return Task.FromResult(ProfilePackageResult.Fail("No page selected."));

        return ExportCoreAsync(PackageKind.TouchPage, new ProfilePackagePayload { TouchPage = page },
            page.Name, targetPath, description);
    }

    public Task<ProfilePackageResult> ExportRotaryPageAsync(RotaryButtonPage page, string targetPath, string description = null)
    {
        if (page == null)
            return Task.FromResult(ProfilePackageResult.Fail("No page selected."));

        return ExportCoreAsync(PackageKind.RotaryPage, new ProfilePackagePayload { RotaryPage = page },
            page.Name, targetPath, description);
    }

    private async Task<ProfilePackageResult> ExportCoreAsync(PackageKind kind, ProfilePackagePayload payload,
        string name, string targetPath, string description)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return ProfilePackageResult.Fail("No target file selected.");

        string stage = Path.Combine(Path.GetTempPath(), "loupixdeck_profile_" + Guid.NewGuid().ToString("N"));
        List<string> warnings = [];

        try
        {
            Directory.CreateDirectory(stage);

            string payloadPath = Path.Combine(stage, ProfilePackageFiles.Payload);

            // Serialization must happen on the UI thread: the exported subtree is made of live
            // ObservableCollections the editor may mutate at any moment, and iterating them
            // off-thread races and throws "Collection was modified". The controller's own
            // SaveConfigAsync marshals for exactly the same reason.
            await Dispatcher.UIThread.InvokeAsync(() => configService.SaveConfig(payload, payloadPath));

            // Read the payload back as JSON. Every scan below then runs against the exact bytes
            // that ship, not against a live object graph that could still change under us.
            JToken payloadToken = JToken.Parse(await File.ReadAllTextAsync(payloadPath));

            ProfilePackageManifest manifest = new()
            {
                FormatVersion = ProfilePackageManifest.CurrentFormatVersion,
                ConfigSchemaVersion = LoupedeckConfig.CurrentVersion,
                Kind = kind,
                Name = string.IsNullOrWhiteSpace(name) ? kind.ToString() : name,
                Description = description ?? string.Empty,
                SourceDeviceSlug = deviceInfo?.Slug,
                SourceDeviceName = deviceInfo?.Name,
                SourceTouchButtonCount = deviceService.TouchButtonCount,
                SourceRotaryButtonCount = deviceService.RotaryButtonCount,
                SourceHasSideStrips = pageManager.HasIndependentRotarySides,
                AppVersion = ResolveAppVersion(),
                ExportedUtc = DateTimeOffset.UtcNow
            };

            manifest.Assets = CopyAssets(payloadToken, stage, warnings);

            // Command names and macros are collected together: a macro's steps can invoke further
            // commands and further macros, so this walks to a fixed point.
            HashSet<string> commandNames = PortableCommandScanner.CollectCommandNames(payloadToken);
            List<Macro> macros = CollectMacros(payloadToken, commandNames, warnings);

            manifest.ReferencedCommands = commandNames.OrderBy(n => n, StringComparer.Ordinal).ToList();
            manifest.Macros = macros.Select(m => m.Name).ToList();
            manifest.RequiredPlugins = CollectRequiredPlugins(payloadToken, commandNames);

            if (macros.Count > 0)
            {
                await File.WriteAllTextAsync(Path.Combine(stage, ProfilePackageFiles.Macros),
                    macroManager.SerializeSubset(macros));
            }

            configService.SaveConfig(manifest, Path.Combine(stage, ProfilePackageFiles.Manifest));

            await Task.Run(() =>
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                ZipFile.CreateFromDirectory(stage, targetPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            });

            return ProfilePackageResult.Ok($"Exported '{manifest.Name}' to {Path.GetFileName(targetPath)}.",
                warnings, targetPath);
        }
        catch (Exception ex)
        {
            return ProfilePackageResult.Fail($"Export failed: {ex.Message}", warnings);
        }
        finally
        {
            TryDeleteDirectory(stage);
        }
    }

    /// <summary>
    /// Copies every asset the payload references into the staging folder, keeping the stored
    /// relative path verbatim, and returns the paths that actually shipped. A reference whose file
    /// is gone on disk is dropped from the manifest and reported — shipping the path without the
    /// file would produce an import that silently renders nothing.
    /// </summary>
    private List<string> CopyAssets(JToken payload, string stage, List<string> warnings)
    {
        List<string> included = [];
        int missing = 0;

        foreach (string relative in AssetPathHarvester.Harvest(payload).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string source = assetService.ResolveAbsolute(relative);
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                missing++;
                continue;
            }

            string destination = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            included.Add(relative);
        }

        if (missing > 0)
        {
            warnings.Add($"{missing} referenced image file(s) were missing on disk and are not included " +
                         "in the package.");
        }

        return included;
    }

    /// <summary>
    /// Resolves the macros the subtree references, transitively: a macro's Command steps can call
    /// further commands and further macros, and those commands feed back into the plugin scan.
    /// <paramref name="commandNames"/> is extended in place with everything the macros reference.
    /// </summary>
    private List<Macro> CollectMacros(JToken payload, HashSet<string> commandNames, List<string> warnings)
    {
        Queue<string> pending = new(PortableCommandScanner.CollectMacroNames(payload));
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<Macro> resolved = [];
        List<string> unresolved = [];

        while (pending.Count > 0)
        {
            string macroName = pending.Dequeue();
            if (!visited.Add(macroName))
                continue;

            Macro macro = macroManager.Get(macroName);
            if (macro == null)
            {
                unresolved.Add(macroName);
                continue;
            }

            resolved.Add(macro);

            // Re-scan the macro through its own serialized form, so the same property-driven
            // scanner sees its Command steps without this code having to know the step model.
            JToken macroToken = JToken.Parse(macroManager.SerializeSubset([macro]));

            foreach (string name in PortableCommandScanner.CollectCommandNames(macroToken))
                commandNames.Add(name);

            foreach (string nested in PortableCommandScanner.CollectMacroNames(macroToken))
            {
                if (!visited.Contains(nested))
                    pending.Enqueue(nested);
            }
        }

        if (unresolved.Count > 0)
        {
            warnings.Add("Referenced macro(s) do not exist here and are not included: " +
                         string.Join(", ", unresolved));
        }

        return resolved;
    }

    /// <summary>
    /// Works out which plugins the package depends on. Two independent sources: the owner of each
    /// referenced command name, and every side-strip binding — a strip provider is bound by plugin
    /// id alone, with no command pointing at it, so a command-only scan would claim the package
    /// needs no plugins at all.
    /// </summary>
    private List<ProfilePackagePluginRequirement> CollectRequiredPlugins(JToken payload, HashSet<string> commandNames)
    {
        Dictionary<string, ProfilePackagePluginRequirement> required =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (LoadedPlugin plugin in pluginManager.Plugins)
        {
            if (plugin?.Manifest?.Id == null)
                continue;

            bool ownsCommand = plugin.Commands.Any(c =>
                c?.Descriptor?.CommandName != null && commandNames.Contains(c.Descriptor.CommandName));

            if (ownsCommand)
                required[plugin.Manifest.Id] = Describe(plugin.Manifest);
        }

        foreach (string stripPluginId in PortableCommandScanner.CollectStripPluginIds(payload))
        {
            if (required.ContainsKey(stripPluginId))
                continue;

            LoadedPlugin plugin = pluginManager.Plugins.FirstOrDefault(p =>
                string.Equals(p?.Manifest?.Id, stripPluginId, StringComparison.OrdinalIgnoreCase));

            // The plugin may not be installed here either (an orphaned strip binding is preserved
            // in the config on purpose). Record what we know so the importer can still name it.
            required[stripPluginId] = plugin?.Manifest != null
                ? Describe(plugin.Manifest)
                : new ProfilePackagePluginRequirement { Id = stripPluginId, Name = stripPluginId };
        }

        return required.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ProfilePackagePluginRequirement Describe(PluginManifest manifest) => new()
    {
        Id = manifest.Id,
        Name = manifest.Name,
        Version = manifest.Version,
        ProjectUrl = manifest.ProjectUrl
    };

    private static string ResolveAppVersion()
    {
        AssemblyInformationalVersionAttribute attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        // Strip the "+<commit>" build metadata, same as the About page does.
        return attribute?.InformationalVersion?.Split('+')[0] ?? "unknown";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProfilePackage] Could not delete temp folder '{path}': {ex.Message}");
        }
    }
}
