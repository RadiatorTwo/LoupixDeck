using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Macros;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.Macros;

/// <inheritdoc cref="IMacroManager"/>
public class MacroManager : IMacroManager
{
    private const string FileName = "macros.json";

    // Characters that would break CommandService's "Name(args)" / "a && b" parsing
    // if they appeared in a macro name.
    private static readonly char[] ForbiddenNameChars = ['(', ')', ',', '&'];

    // Own serializer settings (not ConfigService's) so the step converter never
    // leaks into config.json serialization.
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new MacroStepJsonConverter() }
    };

    private readonly Lock _lock = new();

    private List<Macro> _macros = [];
    private Dictionary<string, Macro> _byName = new(StringComparer.OrdinalIgnoreCase);
    private string _stopHotkey = string.Empty;

    public string StopHotkey
    {
        get
        {
            lock (_lock)
                return _stopHotkey;
        }
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            lock (_lock)
            {
                if (_stopHotkey == normalized)
                    return;
                _stopHotkey = normalized;
            }

            Save();
        }
    }

    public IReadOnlyList<Macro> Macros
    {
        get
        {
            lock (_lock)
            {
                return _macros.ToList();
            }
        }
    }

    public event EventHandler MacrosChanged;

    public void Load()
    {
        var path = FileDialogHelper.GetConfigPath(FileName);

        MacroSettings settings = null;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                settings = JsonConvert.DeserializeObject<MacroSettings>(json, SerializerSettings);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacroManager] Failed to load {path}: {ex.Message}");
            BackupCorruptedFile(path);
        }

        settings ??= new MacroSettings();

        lock (_lock)
        {
            _stopHotkey = settings.StopHotkey?.Trim() ?? string.Empty;

            // Steps with unknown discriminators deserialize to null — drop them.
            _macros = settings.Macros?
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .ToList() ?? [];

            foreach (var macro in _macros)
            {
                var steps = macro.Steps?.Where(s => s != null).ToList() ?? [];
                macro.Steps = new System.Collections.ObjectModel.ObservableCollection<MacroStep>(steps);
            }

            RebuildIndex();
        }
    }

    public Macro Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (_lock)
        {
            return _byName.GetValueOrDefault(name.Trim());
        }
    }

    public void ReplaceAll(IEnumerable<Macro> macros)
    {
        lock (_lock)
        {
            _macros = macros?
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .ToList() ?? [];
            RebuildIndex();
        }

        Save();
    }

    public void Save()
    {
        var path = FileDialogHelper.GetConfigPath(FileName);

        MacroSettings settings;
        lock (_lock)
        {
            settings = new MacroSettings { Macros = _macros.ToList(), StopHotkey = _stopHotkey };
        }

        try
        {
            var json = JsonConvert.SerializeObject(settings, SerializerSettings);

            // Atomic write: temp file first, then move into place (same pattern as ConfigService).
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacroManager] Failed to save {path}: {ex.Message}");
        }

        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Character-level name check (non-empty, no command-parser characters). Used by the
    /// editor for working copies that are not part of the manager's in-memory set.
    /// </summary>
    public static bool HasValidNameCharacters(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(ForbiddenNameChars) < 0;
    }

    public bool IsNameValid(string name, Macro ignore = null)
    {
        if (!HasValidNameCharacters(name))
            return false;

        lock (_lock)
        {
            var existing = _byName.GetValueOrDefault(name.Trim());
            return existing == null || ReferenceEquals(existing, ignore);
        }
    }

    // Caller must hold _lock.
    private void RebuildIndex()
    {
        _byName = new Dictionary<string, Macro>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in _macros)
        {
            _byName.TryAdd(macro.Name.Trim(), macro);
        }
    }

    private static void BackupCorruptedFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Move(path, $"{path}.corrupted.{timestamp}.bak");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacroManager] Failed to backup corrupted file: {ex.Message}");
        }
    }
}
