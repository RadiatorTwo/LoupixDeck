using LoupixDeck.Models;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Reads and writes portable <c>.loupixprofile</c> packages (issue #133): a zip carrying a
/// profile, a workspace or a single page together with the assets, macros and plugin metadata it
/// needs, so it can be backed up, moved to another machine or shared.
/// </summary>
/// <remarks>
/// Device-scoped: exporting needs the device's button geometry for the manifest, and importing
/// writes into this device's config.
/// </remarks>
public interface IProfilePackageService
{
    /// <summary>Writes <paramref name="profile"/> and all of its workspaces to <paramref name="targetPath"/>.</summary>
    Task<ProfilePackageResult> ExportProfileAsync(Profile profile, string targetPath, string description = null);

    /// <summary>Writes <paramref name="workspace"/> and all of its pages to <paramref name="targetPath"/>.</summary>
    Task<ProfilePackageResult> ExportWorkspaceAsync(Workspace workspace, string targetPath, string description = null);

    /// <summary>Writes a single touch page to <paramref name="targetPath"/>.</summary>
    Task<ProfilePackageResult> ExportTouchPageAsync(TouchButtonPage page, string targetPath, string description = null);

    /// <summary>Writes a single rotary page to <paramref name="targetPath"/>.</summary>
    Task<ProfilePackageResult> ExportRotaryPageAsync(RotaryButtonPage page, string targetPath, string description = null);

    /// <summary>
    /// Extracts a package into an isolated staging folder and reports everything the import
    /// preview needs. Touches neither the asset store, nor macros.json, nor the config — nothing
    /// is committed until the returned analysis is handed to the import.
    /// Always returns an analysis; check <see cref="ProfilePackageAnalysis.IsImportable"/>.
    /// </summary>
    Task<ProfilePackageAnalysis> InspectAsync(string packagePath);

    /// <summary>
    /// Commits an inspected package into this device's configuration according to
    /// <paramref name="options"/>, then persists the config. Consumes the analysis: its staging
    /// folder is gone afterwards either way.
    /// </summary>
    Task<ProfilePackageResult> ImportAsync(ProfilePackageAnalysis analysis, ProfilePackageImportOptions options);

    /// <summary>
    /// Throws away the staging folder of an analysis the user cancelled. Safe to call twice.
    /// </summary>
    void DiscardAnalysis(ProfilePackageAnalysis analysis);
}
