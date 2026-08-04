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
    LoupedeckConfig config,
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

    // ─────────────────────────────── Inspection ───────────────────────────────

    public async Task<ProfilePackageAnalysis> InspectAsync(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            return ProfilePackageAnalysis.Blocked(packagePath, "The selected file does not exist.");

        string stage = Path.Combine(Path.GetTempPath(), "loupixdeck_profile_" + Guid.NewGuid().ToString("N"));

        try
        {
            await Task.Run(() => ExtractPackage(packagePath, stage));
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath, $"Could not read the package: {ex.Message}");
        }

        try
        {
            return await Task.Run(() => AnalyzeStagedPackage(packagePath, stage));
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath, $"Could not read the package: {ex.Message}");
        }
    }

    public void DiscardAnalysis(ProfilePackageAnalysis analysis)
    {
        if (!string.IsNullOrEmpty(analysis?.StageDirectory))
            TryDeleteDirectory(analysis.StageDirectory);
    }

    /// <summary>
    /// Extracts the package entry by entry, refusing any entry that would land outside
    /// <paramref name="stage"/>. A package is untrusted input downloaded from the internet, unlike
    /// a config file, so the zip-slip check is made explicitly rather than relying on the runtime.
    /// </summary>
    private static void ExtractPackage(string packagePath, string stage)
    {
        Directory.CreateDirectory(stage);
        string stageRoot = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(packagePath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // Directory entries have an empty name; their path is created with the files.
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string destination = Path.GetFullPath(Path.Combine(stage, entry.FullName));
            if (!destination.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The package contains an unsafe path: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private ProfilePackageAnalysis AnalyzeStagedPackage(string packagePath, string stage)
    {
        string manifestPath = Path.Combine(stage, ProfilePackageFiles.Manifest);
        if (!File.Exists(manifestPath))
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath,
                $"The package has no {ProfilePackageFiles.Manifest}.");
        }

        // Read the envelope version before deserializing anything: a package from a newer build
        // may well contain fields and a payload shape this build would misread.
        JObject manifestJson = JObject.Parse(File.ReadAllText(manifestPath));
        int formatVersion = manifestJson.Value<int?>(nameof(ProfilePackageManifest.FormatVersion))
                            ?? manifestJson.Value<int?>("formatVersion")
                            ?? 0;

        if (formatVersion > ProfilePackageManifest.CurrentFormatVersion)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath,
                $"This package was created by a newer version of LoupixDeck (package format v{formatVersion}, " +
                $"this build understands v{ProfilePackageManifest.CurrentFormatVersion}). Update LoupixDeck to import it.");
        }

        ProfilePackageManifest manifest = manifestJson.ToObject<ProfilePackageManifest>();
        if (manifest == null)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath, "The package manifest is unreadable.");
        }

        string payloadPath = Path.Combine(stage,
            string.IsNullOrWhiteSpace(manifest.PayloadFile) ? ProfilePackageFiles.Payload : manifest.PayloadFile);

        if (!File.Exists(payloadPath))
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath, "The package has no payload.", manifest);
        }

        JObject payloadJson = JObject.Parse(File.ReadAllText(payloadPath));

        // Walk the envelope migration chain. Empty at v1; the loop exists so v2 is additive.
        List<IPackageMigration> applicable = _migrations
            .Where(m => m.FromVersion >= formatVersion)
            .OrderBy(m => m.FromVersion)
            .ToList();

        foreach (IPackageMigration migration in applicable)
            migration.Apply(manifestJson, payloadJson);

        if (applicable.Count > 0)
        {
            // Persist the upgraded documents so the payload is loaded through the very same
            // IConfigService round-trip an un-migrated package takes.
            manifest = manifestJson.ToObject<ProfilePackageManifest>() ?? manifest;
            File.WriteAllText(payloadPath, payloadJson.ToString());
        }

        List<string> warnings = [];

        if (manifest.ConfigSchemaVersion > LoupedeckConfig.CurrentVersion)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath,
                $"This package uses a newer configuration schema (v{manifest.ConfigSchemaVersion}) than this " +
                $"build supports (v{LoupedeckConfig.CurrentVersion}). Update LoupixDeck to import it.", manifest);
        }

        if (manifest.ConfigSchemaVersion > 0 && manifest.ConfigSchemaVersion < LoupedeckConfig.CurrentVersion)
        {
            // Every field added since is additive with a working default, so this is safe — but the
            // config migration chain works on a whole config root and cannot run on a subtree, so
            // the newer settings simply fall back to their defaults.
            warnings.Add($"Created with an older configuration schema (v{manifest.ConfigSchemaVersion} vs " +
                         $"v{LoupedeckConfig.CurrentVersion}); settings added since will use their defaults.");
        }

        // Deserialize through IConfigService so the payload goes through exactly the converter set
        // it was written with (the polymorphic layer converter above all). A parse failure there
        // moves the file aside and yields null — harmless inside the staging folder, but it means
        // "corrupt package", never "empty package".
        ProfilePackagePayload payload = configService.LoadConfig<ProfilePackagePayload>(payloadPath);

        if (payload == null)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath, "The package payload is corrupt.", manifest);
        }

        if (PayloadItem(manifest.Kind, payload) == null)
        {
            TryDeleteDirectory(stage);
            return ProfilePackageAnalysis.Blocked(packagePath,
                $"The package claims to contain a {manifest.Kind} but its payload is empty.", manifest);
        }

        (bool mismatch, string mismatchMessage, int surplus, bool sideStripData) = ClassifyDevice(manifest, payloadJson);
        List<PackagePluginStatus> plugins = ClassifyPlugins(manifest);
        List<string> unknownCommands = ClassifyCommands(manifest, payloadJson);
        List<PackageMacroStatus> macros = ClassifyMacros(stage);

        return new ProfilePackageAnalysis
        {
            PackagePath = packagePath,
            StageDirectory = stage,
            Manifest = manifest,
            Payload = payload,
            IsImportable = true,
            DeviceMismatch = mismatch,
            DeviceMismatchMessage = mismatchMessage,
            SurplusTouchButtons = surplus,
            SideStripDataOnNonStripDevice = sideStripData,
            Plugins = plugins,
            UnknownCommands = unknownCommands,
            Macros = macros,
            Warnings = warnings
        };
    }

    /// <summary>Returns the populated payload slot for a kind, or null when it is empty.</summary>
    private static object PayloadItem(PackageKind kind, ProfilePackagePayload payload) => kind switch
    {
        PackageKind.Profile => payload.Profile,
        PackageKind.Workspace => payload.Workspace,
        PackageKind.TouchPage => payload.TouchPage,
        PackageKind.RotaryPage => payload.RotaryPage,
        _ => null
    };

    private (bool Mismatch, string Message, int Surplus, bool SideStripData) ClassifyDevice(
        ProfilePackageManifest manifest, JObject payloadJson)
    {
        bool sameDevice = string.Equals(manifest.SourceDeviceSlug, deviceInfo?.Slug, StringComparison.OrdinalIgnoreCase);
        int surplus = Math.Max(0, manifest.SourceTouchButtonCount - deviceService.TouchButtonCount);
        bool sideStripData = manifest.SourceHasSideStrips && !pageManager.HasIndependentRotarySides &&
                             HasSideSpecificRotaryPages(payloadJson);

        if (sameDevice && surplus == 0 && !sideStripData)
            return (false, null, 0, false);

        List<string> parts = [];

        if (!sameDevice)
        {
            parts.Add($"Exported from {manifest.SourceDeviceName ?? manifest.SourceDeviceSlug ?? "an unknown device"}; " +
                      $"this device is a {deviceInfo?.Name ?? "different model"}.");
        }

        if (surplus > 0)
        {
            parts.Add($"{surplus} touch key(s) per page have no key on this device. They are kept in the " +
                      "configuration but are not shown, and reappear if you export back to the original device.");
        }

        if (sideStripData)
        {
            parts.Add("The package pages its dial columns independently (side strips); this device does not, " +
                      "so those pages are imported into the shared rotary page list.");
        }

        // Not a blocker on purpose: Live and Live S share most layouts, and a surplus page simply
        // carries buttons this device cannot show.
        return (parts.Count > 0, string.Join(" ", parts), surplus, sideStripData);
    }

    /// <summary>True when the payload contains a rotary page bound to a single side.</summary>
    private static bool HasSideSpecificRotaryPages(JObject payloadJson)
    {
        return payloadJson.DescendantsAndSelf()
            .OfType<JProperty>()
            .Any(p => string.Equals(p.Name, "Side", StringComparison.OrdinalIgnoreCase) &&
                      p.Value is JValue { Type: JTokenType.String } v &&
                      !string.Equals((string)v.Value, nameof(RotarySide.Both), StringComparison.OrdinalIgnoreCase));
    }

    private List<PackagePluginStatus> ClassifyPlugins(ProfilePackageManifest manifest)
    {
        List<PackagePluginStatus> result = [];

        foreach (ProfilePackagePluginRequirement requirement in manifest.RequiredPlugins ?? [])
        {
            if (string.IsNullOrWhiteSpace(requirement?.Id))
                continue;

            LoadedPlugin installed = pluginManager.Plugins.FirstOrDefault(p =>
                string.Equals(p?.Manifest?.Id, requirement.Id, StringComparison.OrdinalIgnoreCase));

            PackagePluginState state;
            if (installed == null)
                state = PackagePluginState.Missing;
            else if (config.EnabledPlugins?.Any(id =>
                         string.Equals(id, requirement.Id, StringComparison.OrdinalIgnoreCase)) == true)
                state = PackagePluginState.Installed;
            else
                state = PackagePluginState.InstalledButDisabled;

            result.Add(new PackagePluginStatus
            {
                Requirement = requirement,
                State = state,
                InstalledVersion = installed?.Manifest?.Version
            });
        }

        return result;
    }

    /// <summary>
    /// Command names the package references that resolve to nothing here. Names owned by a plugin
    /// listed as missing/disabled are left out — the plugin section already explains those, and
    /// repeating them as "unknown" would double-report the same cause.
    /// </summary>
    private List<string> ClassifyCommands(ProfilePackageManifest manifest, JObject payloadJson)
    {
        IEnumerable<string> names = manifest.ReferencedCommands is { Count: > 0 }
            ? manifest.ReferencedCommands
            : PortableCommandScanner.CollectCommandNames(payloadJson);

        HashSet<string> pluginOwned = new(StringComparer.Ordinal);
        foreach (LoadedPlugin plugin in pluginManager.Plugins)
        {
            foreach (PluginSdk.IPluginCommand command in plugin?.Commands ?? [])
            {
                if (command?.Descriptor?.CommandName != null)
                    pluginOwned.Add(command.Descriptor.CommandName);
            }
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !commandRegistry.Contains(name) && !pluginOwned.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private List<PackageMacroStatus> ClassifyMacros(string stage)
    {
        string macroPath = Path.Combine(stage, ProfilePackageFiles.Macros);
        if (!File.Exists(macroPath))
            return [];

        List<PackageMacroStatus> result = [];

        foreach (Macro incoming in macroManager.DeserializeSubset(File.ReadAllText(macroPath)))
        {
            Macro existing = macroManager.Get(incoming.Name);

            PackageMacroState state;
            if (existing == null)
                state = PackageMacroState.New;
            else if (string.Equals(macroManager.SerializeSubset([existing]),
                         macroManager.SerializeSubset([incoming]), StringComparison.Ordinal))
                state = PackageMacroState.Identical;
            else
                state = PackageMacroState.Conflicting;

            result.Add(new PackageMacroStatus { Name = incoming.Name, State = state });
        }

        return result;
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
