using System.IO.Compression;
using System.Reflection;
using Avalonia.Threading;
using LoupixDeck.Controllers;
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
    IWorkspaceActivationService workspaceActivation,
    IDeviceController controller,
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

    // ───────────────────────────────── Import ─────────────────────────────────

    public async Task<ProfilePackageResult> ImportAsync(ProfilePackageAnalysis analysis,
        ProfilePackageImportOptions options)
    {
        if (analysis == null || !analysis.IsImportable)
            return ProfilePackageResult.Fail(analysis?.BlockReason ?? "The package cannot be imported.");

        options ??= new ProfilePackageImportOptions();
        List<string> warnings = [.. analysis.Warnings];

        try
        {
            // 1. Back the target up before anything is written. The backup IS an export, so it can
            //    be restored through the very same import dialog. A failed backup aborts the
            //    import — the point of the backup is that it exists before the damage.
            if (options.Mode == PackageImportMode.Replace && options.BackupReplacedItem)
            {
                ProfilePackageResult backup = await BackupReplaceTargetAsync(analysis, options);
                if (backup != null && !backup.Success)
                    return ProfilePackageResult.Fail($"Import aborted: the backup failed ({backup.Message}).", warnings);

                if (backup?.PackagePath != null)
                    warnings.Add($"Previous version backed up to {backup.PackagePath}.");
            }

            // 2. Macros. Written before the payload is attached because the payload's macro
            //    references must be rewritten with the resolutions applied here.
            Dictionary<string, string> macroRenames = ApplyMacros(analysis, options, warnings);

            // 3. Assets into the (content-addressed) store. Import is a no-op for anything this
            //    machine already has, so a same-machine re-import copies nothing.
            Dictionary<string, string> assetMap = ImportAssets(analysis, warnings);

            // 4. Rewrite the staged payload: fresh ids (seeded for Replace), macro renames and any
            //    asset path the store handed back under a different name — one JSON pass.
            ProfilePackagePayload payload = RewriteAndLoadPayload(analysis, options, macroRenames, assetMap);
            if (payload == null)
                return ProfilePackageResult.Fail("The package payload could not be prepared for import.", warnings);

            // 5+6. Attach and persist on the UI thread. Everything below is ObservableCollections
            //      with setter-driven wiring, and the config must reach disk in the same
            //      continuation — see the asset-cleanup note on PersistImport.
            ProfilePackageResult result = await Dispatcher.UIThread.InvokeAsync(() =>
                AttachPayload(analysis, options, payload, warnings));

            return result;
        }
        catch (Exception ex)
        {
            return ProfilePackageResult.Fail($"Import failed: {ex.Message}", warnings);
        }
        finally
        {
            DiscardAnalysis(analysis);
        }
    }

    private Task<ProfilePackageResult> BackupReplaceTargetAsync(ProfilePackageAnalysis analysis,
        ProfilePackageImportOptions options)
    {
        string backupDir = Path.Combine(FileDialogHelper.GetConfigDir(), "backups");
        Directory.CreateDirectory(backupDir);

        string Target(string name) => Path.Combine(backupDir,
            $"{SafeFileName(deviceInfo?.Slug ?? "device")}_{analysis.Manifest.Kind}_{SafeFileName(name)}_" +
            $"{DateTime.Now:yyyyMMdd_HHmmss}.{ProfilePackageFiles.Extension}");

        switch (analysis.Manifest.Kind)
        {
            case PackageKind.Profile:
            {
                Profile profile = FindProfile(options.ReplaceTargetProfileId);
                return profile == null
                    ? Task.FromResult<ProfilePackageResult>(null)
                    : ExportProfileAsync(profile, Target(profile.Name), "Automatic backup before import.");
            }

            case PackageKind.Workspace:
            {
                Workspace workspace = FindWorkspace(options.ReplaceTargetWorkspaceId);
                return workspace == null
                    ? Task.FromResult<ProfilePackageResult>(null)
                    : ExportWorkspaceAsync(workspace, Target(workspace.Name), "Automatic backup before import.");
            }

            case PackageKind.TouchPage:
            {
                TouchButtonPage page = TouchPageAt(options);
                return page == null
                    ? Task.FromResult<ProfilePackageResult>(null)
                    : ExportTouchPageAsync(page, Target(page.Name), "Automatic backup before import.");
            }

            case PackageKind.RotaryPage:
            {
                RotaryButtonPage page = RotaryPageAt(analysis, options);
                return page == null
                    ? Task.FromResult<ProfilePackageResult>(null)
                    : ExportRotaryPageAsync(page, Target(page.Name), "Automatic backup before import.");
            }

            default:
                return Task.FromResult<ProfilePackageResult>(null);
        }
    }

    /// <summary>
    /// Merges the package's macros into the local set according to the user's per-macro decisions
    /// and returns the old → new names of the renamed ones. Persists macros.json immediately —
    /// <c>ReplaceAll</c> is the only mutation path there.
    /// </summary>
    private Dictionary<string, string> ApplyMacros(ProfilePackageAnalysis analysis,
        ProfilePackageImportOptions options, List<string> warnings)
    {
        Dictionary<string, string> renames = new(StringComparer.OrdinalIgnoreCase);

        string macroPath = Path.Combine(analysis.StageDirectory, ProfilePackageFiles.Macros);
        if (!File.Exists(macroPath))
            return renames;

        IReadOnlyList<Macro> incoming = macroManager.DeserializeSubset(File.ReadAllText(macroPath));
        if (incoming.Count == 0)
            return renames;

        List<Macro> working = macroManager.Macros.ToList();
        List<string> skipped = [];
        bool changed = false;

        foreach (Macro macro in incoming)
        {
            Macro existing = working.FirstOrDefault(m =>
                string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                working.Add(macro);
                changed = true;
                continue;
            }

            // Identical content needs no decision — the local macro already is the incoming one.
            if (string.Equals(macroManager.SerializeSubset([existing]), macroManager.SerializeSubset([macro]),
                    StringComparison.Ordinal))
            {
                continue;
            }

            MacroConflictResolution resolution =
                options.MacroResolutions.TryGetValue(macro.Name, out MacroConflictResolution chosen)
                    ? chosen
                    : MacroConflictResolution.Skip;

            switch (resolution)
            {
                case MacroConflictResolution.Replace:
                    working[working.IndexOf(existing)] = macro;
                    changed = true;
                    break;

                case MacroConflictResolution.Rename:
                    string newName = ResolveRenameTarget(macro.Name, options, working);
                    renames[macro.Name] = newName;
                    macro.Name = newName;
                    working.Add(macro);
                    changed = true;
                    break;

                default:
                    skipped.Add(macro.Name);
                    break;
            }
        }

        if (skipped.Count > 0)
        {
            // The reference is left in place on purpose: the runtime tolerates an unknown command
            // (it falls through to the shell runner) and the editor keeps it as free text, so the
            // binding survives if the macro is added later.
            warnings.Add("Skipped macro(s) — buttons referencing them keep an unresolved reference: " +
                         string.Join(", ", skipped));
        }

        if (changed)
            macroManager.ReplaceAll(working);

        return renames;
    }

    private static string ResolveRenameTarget(string originalName, ProfilePackageImportOptions options,
        List<Macro> working)
    {
        string requested = options.MacroRenames.GetValueOrDefault(originalName);

        bool Taken(string name) => working.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(requested) && MacroManager.HasValidNameCharacters(requested) && !Taken(requested))
            return requested.Trim();

        // Fall back to a suffix so a missing or unusable rename never silently overwrites.
        string candidate = $"{originalName} (imported)";
        int suffix = 2;
        while (Taken(candidate))
            candidate = $"{originalName} (imported {suffix++})";

        return candidate;
    }

    /// <summary>
    /// Moves the package's assets into the local store and returns any path that changed on the
    /// way in. Content addressing makes an already-present asset a free no-op.
    /// </summary>
    private Dictionary<string, string> ImportAssets(ProfilePackageAnalysis analysis, List<string> warnings)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        int missing = 0;

        foreach (string relative in analysis.Manifest.Assets ?? [])
        {
            string staged = Path.Combine(analysis.StageDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(staged))
            {
                missing++;
                continue;
            }

            string subFolder = SubFolderOf(relative);
            string imported = assetService.Import(staged, subFolder);

            if (!string.IsNullOrEmpty(imported) &&
                !string.Equals(imported, relative, StringComparison.OrdinalIgnoreCase))
            {
                map[relative] = imported;
            }
        }

        if (missing > 0)
            warnings.Add($"{missing} image file(s) listed in the package were not inside it.");

        return map;
    }

    /// <summary>Returns the asset sub-folder of a stored path ("wallpapers"), or null at the root.</summary>
    private static string SubFolderOf(string relativePath)
    {
        string[] parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // parts[0] is the "assets" prefix, the last part is the file name.
        return parts.Length > 2 ? string.Join('/', parts[1..^1]) : null;
    }

    private ProfilePackagePayload RewriteAndLoadPayload(ProfilePackageAnalysis analysis,
        ProfilePackageImportOptions options, Dictionary<string, string> macroRenames,
        Dictionary<string, string> assetMap)
    {
        string payloadPath = Path.Combine(analysis.StageDirectory,
            string.IsNullOrWhiteSpace(analysis.Manifest.PayloadFile)
                ? ProfilePackageFiles.Payload
                : analysis.Manifest.PayloadFile);

        if (!File.Exists(payloadPath))
            return null;

        JObject payloadJson = JObject.Parse(File.ReadAllText(payloadPath));

        // Replace keeps the target's identity so everything already pointing at it — active and
        // startup profile, fallback profile, context rules — keeps resolving after the swap.
        Dictionary<Guid, Guid> seed = [];
        if (options.Mode == PackageImportMode.Replace)
        {
            Guid? incoming = IncomingRootId(analysis, payloadJson);
            Guid? target = analysis.Manifest.Kind switch
            {
                PackageKind.Profile => options.ReplaceTargetProfileId,
                PackageKind.Workspace => options.ReplaceTargetWorkspaceId,
                _ => null
            };

            if (incoming.HasValue && target.HasValue)
                seed[incoming.Value] = target.Value;
        }

        PortableIdRemapper.Remap(payloadJson, seed);
        PortablePayloadRewriter.RenameMacroReferences(payloadJson, macroRenames);
        PortablePayloadRewriter.RemapAssetPaths(payloadJson, assetMap);

        File.WriteAllText(payloadPath, payloadJson.ToString());

        return configService.LoadConfig<ProfilePackagePayload>(payloadPath);
    }

    private static Guid? IncomingRootId(ProfilePackageAnalysis analysis, JObject payloadJson)
    {
        string slot = analysis.Manifest.Kind switch
        {
            PackageKind.Profile => nameof(ProfilePackagePayload.Profile),
            PackageKind.Workspace => nameof(ProfilePackagePayload.Workspace),
            _ => null
        };

        if (slot == null)
            return null;

        JToken id = payloadJson[slot]?["Id"];
        return id != null && Guid.TryParse(id.ToString(), out Guid parsed) ? parsed : null;
    }

    /// <summary>
    /// Attaches the prepared payload to the config and persists it. UI thread.
    /// </summary>
    /// <remarks>
    /// Saving here is not optional. <c>AssetService.Cleanup</c> is a mark-and-sweep over the whole
    /// asset tree whose keep-set is built from the config files <i>on disk</i>, and it runs on
    /// every save of every device. Between importing the assets and writing this config, the new
    /// files are referenced by nothing on disk — another device saving in that window would delete
    /// them and leave the imported item rendering blanks.
    /// </remarks>
    private ProfilePackageResult AttachPayload(ProfilePackageAnalysis analysis,
        ProfilePackageImportOptions options, ProfilePackagePayload payload, List<string> warnings)
    {
        int touchCount = deviceService.TouchButtonCount;
        int rotaryCount = deviceService.RotaryButtonCount;
        int sideCount = pageManager.SideRotaryButtonCount;
        string name = string.IsNullOrWhiteSpace(options.NewName) ? analysis.Manifest.Name : options.NewName.Trim();

        EnablePlugins(analysis, options, warnings);

        Guid? importedProfileId = null;
        string what;

        switch (analysis.Manifest.Kind)
        {
            case PackageKind.Profile:
            {
                Profile profile = payload.Profile;
                profile.Name = name;
                PortablePayloadNormalizer.Normalize(profile, touchCount, rotaryCount, sideCount);
                importedProfileId = profile.Id;

                Profile existing = options.Mode == PackageImportMode.Replace
                    ? FindProfile(options.ReplaceTargetProfileId)
                    : null;

                if (existing != null)
                {
                    int index = config.Profiles.IndexOf(existing);
                    bool wasActive = config.ActiveProfileId == existing.Id;

                    config.Profiles.RemoveAt(index);
                    config.Profiles.Insert(index, profile);

                    if (wasActive)
                        workspaceActivation.ActivateProfile(profile.Id);

                    warnings.Add("Buttons in other profiles that jump to a specific workspace of the " +
                                 "replaced profile may no longer resolve.");
                }
                else
                {
                    config.Profiles.Add(profile);
                }

                what = $"profile '{profile.Name}'";
                break;
            }

            case PackageKind.Workspace:
            {
                Workspace workspace = payload.Workspace;
                workspace.Name = name;
                PortablePayloadNormalizer.Normalize(workspace, touchCount, rotaryCount, sideCount);

                Profile owner = FindProfile(options.TargetProfileId) ?? config.ActiveProfile;
                if (owner == null)
                    return ProfilePackageResult.Fail("There is no profile to import the workspace into.", warnings);

                Workspace existing = options.Mode == PackageImportMode.Replace
                    ? owner.Workspaces.FirstOrDefault(w => w.Id == options.ReplaceTargetWorkspaceId)
                    : null;

                if (existing != null)
                {
                    int index = owner.Workspaces.IndexOf(existing);
                    owner.Workspaces.RemoveAt(index);
                    owner.Workspaces.Insert(index, workspace);
                }
                else
                {
                    owner.Workspaces.Add(workspace);
                }

                importedProfileId = owner.Id;
                what = $"workspace '{workspace.Name}' into profile '{owner.Name}'";
                break;
            }

            case PackageKind.TouchPage:
            {
                TouchButtonPage page = payload.TouchPage;
                page.Name = name;
                PortablePayloadNormalizer.Normalize(page, touchCount);

                Workspace target = FindWorkspaceById(options.TargetWorkspaceId) ?? config.ActiveWorkspace;
                if (target == null)
                    return ProfilePackageResult.Fail("There is no workspace to import the page into.", warnings);

                InsertOrReplace(target.TouchButtonPages, page, options);
                what = $"touch page '{page.Name}' into workspace '{target.Name}'";
                break;
            }

            case PackageKind.RotaryPage:
            {
                RotaryButtonPage page = payload.RotaryPage;
                page.Name = name;

                // On a device without side strips there is only the shared list, so a page bound to
                // one column would otherwise land somewhere nothing ever displays.
                if (!pageManager.HasIndependentRotarySides && page.Side != RotarySide.Both)
                {
                    page.Side = RotarySide.Both;
                    warnings.Add("The page was bound to a single dial column; this device has none, " +
                                 "so it was imported into the shared rotary pages.");
                }

                PortablePayloadNormalizer.Normalize(page, rotaryCount, sideCount);

                Workspace target = FindWorkspaceById(options.TargetWorkspaceId) ?? config.ActiveWorkspace;
                if (target == null)
                    return ProfilePackageResult.Fail("There is no workspace to import the page into.", warnings);

                InsertOrReplace(RotaryPagesOf(target, page.Side), page, options);
                what = $"rotary page '{page.Name}' into workspace '{target.Name}'";
                break;
            }

            default:
                return ProfilePackageResult.Fail("Unsupported package content.", warnings);
        }

        controller.SaveConfig();

        return ProfilePackageResult.Ok($"Imported {what}.", warnings, importedProfileId: importedProfileId);
    }

    private void EnablePlugins(ProfilePackageAnalysis analysis, ProfilePackageImportOptions options,
        List<string> warnings)
    {
        if (!options.EnableRequiredPlugins)
            return;

        config.EnabledPlugins ??= [];
        List<string> enabled = [];

        foreach (PackagePluginStatus status in analysis.Plugins
                     .Where(p => p.State == PackagePluginState.InstalledButDisabled))
        {
            config.EnabledPlugins.Add(status.Requirement.Id);
            enabled.Add(status.Requirement.Name ?? status.Requirement.Id);
        }

        if (enabled.Count > 0)
        {
            warnings.Add("Enabled for this device (takes effect after a restart): " + string.Join(", ", enabled));
        }
    }

    private static void InsertOrReplace<T>(IList<T> pages, T page, ProfilePackageImportOptions options)
        where T : ButtonPageBase
    {
        if (options.Mode == PackageImportMode.Replace &&
            options.ReplaceTargetPageIndex is { } index &&
            index >= 0 && index < pages.Count)
        {
            pages.RemoveAt(index);
            pages.Insert(index, page);
        }
        else
        {
            pages.Add(page);
        }

        for (int i = 0; i < pages.Count; i++)
            pages[i].Page = i + 1;
    }

    private Profile FindProfile(Guid? id) =>
        id.HasValue ? config.Profiles?.FirstOrDefault(p => p.Id == id.Value) : null;

    private Workspace FindWorkspace(Guid? id) => FindWorkspaceById(id);

    private Workspace FindWorkspaceById(Guid? id) =>
        id.HasValue
            ? config.Profiles?.SelectMany(p => p.Workspaces ?? []).FirstOrDefault(w => w.Id == id.Value)
            : null;

    private static IList<RotaryButtonPage> RotaryPagesOf(Workspace workspace, RotarySide side) => side switch
    {
        RotarySide.Left => workspace.LeftRotaryButtonPages,
        RotarySide.Right => workspace.RightRotaryButtonPages,
        _ => workspace.RotaryButtonPages
    };

    private TouchButtonPage TouchPageAt(ProfilePackageImportOptions options)
    {
        Workspace workspace = FindWorkspaceById(options.TargetWorkspaceId) ?? config.ActiveWorkspace;
        int? index = options.ReplaceTargetPageIndex;

        return workspace?.TouchButtonPages != null && index is >= 0 && index < workspace.TouchButtonPages.Count
            ? workspace.TouchButtonPages[index.Value]
            : null;
    }

    private RotaryButtonPage RotaryPageAt(ProfilePackageAnalysis analysis, ProfilePackageImportOptions options)
    {
        Workspace workspace = FindWorkspaceById(options.TargetWorkspaceId) ?? config.ActiveWorkspace;
        if (workspace == null)
            return null;

        RotarySide side = analysis.Payload?.RotaryPage?.Side ?? RotarySide.Both;
        IList<RotaryButtonPage> pages = RotaryPagesOf(workspace, side);
        int? index = options.ReplaceTargetPageIndex;

        return pages != null && index is >= 0 && index < pages.Count ? pages[index.Value] : null;
    }

    /// <summary>Strips characters that are not valid in a file name (a profile can be "OBS / Live").</summary>
    private static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "profile";

        string cleaned = new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim();
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
