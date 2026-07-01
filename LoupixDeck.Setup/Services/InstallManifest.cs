using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Small record of what an install placed on disk, written to
/// <c>&lt;installDir&gt;\install-manifest.json</c>. Update/repair/uninstall read it back to know the
/// previous version, which plugins were installed, and which shortcuts to remove. Serialized with
/// System.Text.Json source generation so it stays NativeAOT-safe.
/// </summary>
public sealed class InstallManifest
{
    public string Version { get; set; } = "0.0.0";
    public string InstallDir { get; set; } = "";
    public bool DesktopShortcut { get; set; }
    public bool StartMenuShortcut { get; set; }

    /// <summary>Whether LoupixDeck was registered to run at Windows startup. Optional; defaults false
    /// for manifests written before autostart existed.</summary>
    public bool Autostart { get; set; }

    /// <summary>Plugin ids the setup wrote into the user plugins dir.</summary>
    public List<string> Plugins { get; set; } = new();

    public string InstalledAtUtc { get; set; } = "";

    public static InstallManifest? TryLoad(string installDir)
    {
        try
        {
            string path = Path.Combine(installDir, AppPaths.InstallManifestName);
            if (!File.Exists(path))
                return null;

            using FileStream fs = File.OpenRead(path);
            return JsonSerializer.Deserialize(fs, SetupJsonContext.Default.InstallManifest);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string installDir)
    {
        string path = Path.Combine(installDir, AppPaths.InstallManifestName);
        using FileStream fs = File.Create(path);
        JsonSerializer.Serialize(fs, this, SetupJsonContext.Default.InstallManifest);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstallManifest))]
internal sealed partial class SetupJsonContext : JsonSerializerContext;
