using LoupixDeck.Models;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.DialPresets;

/// <inheritdoc cref="IDialPresetStore"/>
public sealed class DialPresetStore : IDialPresetStore
{
    private const string FileName = "dial-presets.json";

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented
    };

    private readonly Lock _lock = new();

    private List<DialPreset> _userPresets = [];

    public IReadOnlyList<DialPreset> Presets
    {
        get
        {
            lock (_lock)
                return [.. _userPresets];
        }
    }

    public event EventHandler PresetsChanged;

    public void Load()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);

        DialPresetFile file = null;
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                file = JsonConvert.DeserializeObject<DialPresetFile>(json, SerializerSettings);
            }
        }
        catch (Exception ex)
        {
            // A broken file must not stop the app from starting: keep it for inspection and carry
            // on with the built-ins alone.
            Console.WriteLine($"[DialPresets] Failed to load {path}: {ex.Message}");
            BackupCorruptedFile(path);
        }

        lock (_lock)
            _userPresets = Sanitize(file?.Presets);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops the entries a hand-edited or partially written file can contain — nameless ones,
    /// ones that would leave every gesture unassigned, and duplicate ids — and gives an entry
    /// written before ids existed one of its own, so loading stays forward-tolerant.
    /// </summary>
    private static List<DialPreset> Sanitize(IEnumerable<DialPreset> presets)
    {
        List<DialPreset> result = [];
        HashSet<Guid> seen = [];

        foreach (DialPreset preset in presets ?? [])
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.IsEmpty)
                continue;

            if (preset.Id == Guid.Empty)
                preset.Id = Guid.NewGuid();

            if (!seen.Add(preset.Id))
                continue;

            preset.Name = preset.Name.Trim();
            result.Add(preset);
        }

        return result;
    }

    public void Add(DialPreset preset)
    {
        if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.IsEmpty)
            return;

        lock (_lock)
        {
            DialPreset stored = preset.Clone();
            stored.Name = stored.Name.Trim();

            if (stored.Id == Guid.Empty)
                stored.Id = Guid.NewGuid();

            _userPresets.Add(stored);
        }

        Save();
    }

    public void Update(DialPreset preset)
    {
        if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.IsEmpty)
            return;

        lock (_lock)
        {
            int index = _userPresets.FindIndex(p => p.Id == preset.Id);
            if (index < 0)
                return;

            DialPreset stored = preset.Clone();
            stored.Name = stored.Name.Trim();
            _userPresets[index] = stored;
        }

        Save();
    }

    public void Remove(Guid id)
    {
        lock (_lock)
        {
            if (_userPresets.RemoveAll(p => p.Id == id) == 0)
                return;
        }

        Save();
    }

    public bool IsNameValid(string name, DialPreset ignore = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string trimmed = name.Trim();

        // Checked against every built-in, not only the ones this platform can run, so a name does
        // not become valid or invalid depending on the machine the preset was made on.
        if (DialPresetLibrary.IsBuiltInName(trimmed))
            return false;

        lock (_lock)
        {
            return !_userPresets.Any(p =>
                p.Id != ignore?.Id &&
                string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void Save()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);

        DialPresetFile file;
        lock (_lock)
            file = new DialPresetFile { Presets = [.. _userPresets] };

        try
        {
            string json = JsonConvert.SerializeObject(file, SerializerSettings);

            // Atomic write: temp file first, then move into place (the same pattern ConfigService
            // and MacroManager use), so an interrupted save cannot truncate the presets.
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DialPresets] Failed to save {path}: {ex.Message}");
        }

        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void BackupCorruptedFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Move(path, $"{path}.corrupted.{timestamp}.bak");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DialPresets] Failed to backup corrupted file: {ex.Message}");
        }
    }

    /// <summary>The file's shape. Wrapped in an object rather than written as a bare array so the
    /// format can grow a field later without breaking the files written today.</summary>
    private sealed class DialPresetFile
    {
        public List<DialPreset> Presets { get; set; } = [];
    }
}
