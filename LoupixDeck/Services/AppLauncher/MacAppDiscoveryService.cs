namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Application discovery on macOS: the <c>.app</c> bundles under the standard application
/// directories.
/// </summary>
/// <remarks>
/// <para>
/// The display name comes from the bundle's own directory name rather than from
/// <c>Info.plist</c>. That is what Finder shows, and it avoids parsing a file that is usually
/// a binary plist — which would mean either a plist reader or one <c>defaults</c> process per
/// bundle, across a directory that routinely holds several hundred of them.
/// </para>
/// <para>
/// The scan descends one level into subdirectories, because vendors group bundles into folders
/// (Utilities, or a suite's own folder), but never descends into a bundle: a <c>.app</c> is
/// itself a directory, and several ship helper bundles inside that are not separately
/// launchable.
/// </para>
/// </remarks>
public sealed class MacAppDiscoveryService : AppDiscoveryServiceBase
{
    private const string BundleExtension = ".app";

    public override bool IsSupported => OperatingSystem.IsMacOS();

    protected override IEnumerable<InstalledApp> Discover(CancellationToken cancellationToken)
    {
        foreach (string root in Roots())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string bundle in BundlesUnder(root, cancellationToken))
            {
                InstalledApp app = Describe(bundle);
                if (app != null)
                    yield return app;
            }
        }
    }

    private static IEnumerable<string> Roots()
    {
        yield return "/Applications";
        yield return "/System/Applications";

        string home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, "Applications");
    }

    /// <summary>
    /// Bundles directly under <paramref name="root"/> plus those one level down, skipping
    /// anything unreadable. A directory that is itself a bundle is never descended into.
    /// </summary>
    private static IEnumerable<string> BundlesUnder(string root, CancellationToken cancellationToken)
    {
        foreach (string entry in SafeDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBundle(entry))
            {
                yield return entry;
                continue;
            }

            foreach (string nested in SafeDirectories(entry))
            {
                if (IsBundle(nested))
                    yield return nested;
            }
        }
    }

    private static bool IsBundle(string path)
        => path.EndsWith(BundleExtension, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A protected or transient directory must not abort the whole scan.
            return [];
        }
    }

    private static InstalledApp Describe(string bundle)
    {
        string name = Path.GetFileNameWithoutExtension(bundle);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new InstalledApp
        {
            Name = name,
            // LaunchAppCommand routes a .app through `open`, which resolves the bundle to the
            // executable inside it.
            Target = bundle,
            Source = AppSource.Installed,
            IconExtractSource = FindIcon(bundle)
        };
    }

    /// <summary>
    /// The bundle's icon file. Preference order matters: most bundles ship several
    /// <c>.icns</c> files (document types, helpers), and only the application's own icon
    /// belongs in the picker — so the conventional names are tried before falling back to
    /// whatever is there.
    /// </summary>
    private static string FindIcon(string bundle)
    {
        string resources = Path.Combine(bundle, "Contents", "Resources");

        string[] candidates =
        [
            Path.Combine(resources, "AppIcon.icns"),
            Path.Combine(resources, $"{Path.GetFileNameWithoutExtension(bundle)}.icns"),
            Path.Combine(resources, "app.icns"),
            Path.Combine(resources, "icon.icns")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            return Directory.EnumerateFiles(resources, "*.icns").FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
