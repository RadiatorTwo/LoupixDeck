namespace LoupixDeck.Services.Portable;

/// <summary>
/// Outcome of an export or import operation, shaped like <c>PluginActionResult</c> so the settings
/// UI can render it through the same message surface the plugin manager already uses.
/// </summary>
public sealed class ProfilePackageResult
{
    public bool Success { get; init; }

    /// <summary>Human-readable summary shown to the user.</summary>
    public string Message { get; init; }

    /// <summary>
    /// Non-fatal problems the user should know about (a referenced image that was missing on disk,
    /// a macro that could not be resolved, a skipped macro leaving a dangling reference).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Absolute path of the written package (export only).</summary>
    public string PackagePath { get; init; }

    /// <summary>Id of the profile that was added or replaced (profile import only).</summary>
    public Guid? ImportedProfileId { get; init; }

    public static ProfilePackageResult Ok(string message, IReadOnlyList<string> warnings = null,
        string packagePath = null, Guid? importedProfileId = null) =>
        new()
        {
            Success = true,
            Message = message,
            Warnings = warnings ?? [],
            PackagePath = packagePath,
            ImportedProfileId = importedProfileId
        };

    public static ProfilePackageResult Fail(string message, IReadOnlyList<string> warnings = null) =>
        new() { Success = false, Message = message, Warnings = warnings ?? [] };
}
