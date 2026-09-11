using LoupixDeck.Utils;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Applications the user added by hand, for everything discovery does not find — a portable program
/// in a folder nobody indexes, a launcher script, a shortcut kept outside the Start Menu.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="IAppDiscoveryService"/> on purpose: a scan result is disposable and
/// may differ on every run, while these entries only exist because the user said so, and must
/// survive both a rescan and a restart. They live in their own file, so nothing that reads or
/// writes the device configs is affected by them.
/// </remarks>
public interface ICustomAppStore
{
    /// <summary>The stored applications, read from disk on first use.</summary>
    IReadOnlyList<InstalledApp> Apps { get; }

    /// <summary>
    /// Adds the program at <paramref name="path"/> and saves. Returns the entry, or null when the
    /// path is unusable or already known — identity is the launch target, as it is for a scanned
    /// application, so adding the same program twice is a no-op rather than a duplicate row.
    /// </summary>
    InstalledApp Add(string path);

    /// <summary>Removes a stored application and saves. Ignores one that is not stored.</summary>
    void Remove(InstalledApp app);

    /// <summary>True when this application came from here rather than from a scan.</summary>
    bool Contains(InstalledApp app);
}

/// <inheritdoc cref="ICustomAppStore"/>
public sealed class CustomAppStore(IConfigService configService) : ICustomAppStore
{
    private const string FileName = "custom-apps.json";

    /// <summary>One stored entry. Deliberately only what the user chose — everything else about an
    /// application is derived, and deriving it again on load keeps the file small and stable.</summary>
    private sealed class Entry
    {
        public string Name { get; set; }
        public string Target { get; set; }
    }

    private List<InstalledApp> _apps;

    public IReadOnlyList<InstalledApp> Apps => _apps ??= Load();

    public InstalledApp Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        InstalledApp app = Describe(path);
        if (app == null)
            return null;

        List<InstalledApp> apps = (List<InstalledApp>)Apps;
        if (apps.Any(existing => existing.Identity == app.Identity))
            return null;

        apps.Add(app);
        Save();
        return app;
    }

    public void Remove(InstalledApp app)
    {
        if (app == null)
            return;

        List<InstalledApp> apps = (List<InstalledApp>)Apps;
        if (apps.RemoveAll(stored => stored.Identity == app.Identity) > 0)
            Save();
    }

    public bool Contains(InstalledApp app)
        => app != null && Apps.Any(stored => stored.Identity == app.Identity);

    /// <summary>
    /// Builds the application record for a picked file. The display name is the file name without
    /// its extension, which is what the system's own menus show for a program nobody registered.
    /// </summary>
    private static InstalledApp Describe(string path)
    {
        string name;
        try
        {
            name = Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new InstalledApp
        {
            Name = name,
            Target = path,
            Source = AppSource.Installed,
            // The picked file is also what an icon can be pulled from, unless it is a desktop entry,
            // where the entry itself names the icon and the extractor resolves it.
            IconExtractSource = path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? null : path
        };
    }

    private List<InstalledApp> Load()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);
        if (!File.Exists(path))
            return [];

        try
        {
            List<Entry> entries = configService.LoadConfig<List<Entry>>(path);
            return entries?
                .Where(entry => !string.IsNullOrWhiteSpace(entry?.Target))
                .Select(entry => new InstalledApp
                {
                    Name = string.IsNullOrWhiteSpace(entry.Name)
                        ? Path.GetFileNameWithoutExtension(entry.Target)
                        : entry.Name,
                    Target = entry.Target,
                    Source = AppSource.Installed,
                    IconExtractSource = entry.Target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : entry.Target
                })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            // A damaged file must not take the panel down with it; the user can add the entries
            // again, which is less bad than an application that will not start.
            Console.WriteLine($"[CustomApps] '{path}' could not be read: {ex.Message}");
            return [];
        }
    }

    private void Save()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);

        try
        {
            configService.SaveConfig(Apps.Select(app => new Entry { Name = app.Name, Target = app.Target }).ToList(),
                path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CustomApps] '{path}' could not be written: {ex.Message}");
        }
    }
}
