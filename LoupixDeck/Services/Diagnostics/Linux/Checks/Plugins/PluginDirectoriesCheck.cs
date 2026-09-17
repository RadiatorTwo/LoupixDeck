using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Plugins;

/// <summary>
/// The two roots plugins are discovered from: the bundled folder next to the executable and the
/// user folder beside the config. A plugin that is "not there" is most often a plugin in a
/// folder the app never looks at, or one it cannot read.
///
/// The bundled root is optional - a source build has none - so only the user root failing to be
/// readable is a problem.
/// </summary>
public sealed class PluginDirectoriesCheck : ILinuxDiagnosticCheck
{
    public string Id => "plugins.directories";

    public DiagnosticCategory Category => DiagnosticCategory.Plugins;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        (string bundled, string user) = PluginManager.GetPluginRoots();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["bundled_root"] = bundled,
            ["user_root"] = user,
            ["bundled_state"] = State(bundled),
            ["user_state"] = State(user)
        };

        if (!Directory.Exists(user))
        {
            // The user root is created at startup, so a missing one means the config directory
            // itself could not be written.
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_PluginUserRootMissing"), user, null, evidence,
                Loc.Tr("Diagnostics_ValueMissing")));
        }

        if (!CanList(user))
        {
            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_PluginUserRootUnreadable"), user, null, evidence,
                Loc.Tr("Diagnostics_ValueNoAccess")));
        }

        int folders = Count(user) + Count(bundled);

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_PluginRootsOkFmt", folders), null, evidence,
            Loc.Tr("Diagnostics_ValueFoldersFmt", folders)));
    }

    private static string State(string path)
        => !Directory.Exists(path) ? "missing" : CanList(path) ? "readable" : "unreadable";

    private static bool CanList(string path)
    {
        try
        {
            Directory.EnumerateFileSystemEntries(path).FirstOrDefault();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Count(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count() : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
