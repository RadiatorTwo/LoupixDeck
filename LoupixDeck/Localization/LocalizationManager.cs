using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;
using LoupixDeck.Utils;

namespace LoupixDeck.Localization;

/// <summary>
/// App-wide UI translation store. Strings are keyed by a stable identifier (see the
/// <c>Localization/Strings/*.json</c> dictionaries); the English file is the base and the
/// fallback, and every other language overrides only the keys it defines.
///
/// The manager is a singleton that raises <see cref="INotifyPropertyChanged"/> when the language
/// changes, so the bindings created by <see cref="TrExtension"/> re-read their value and the whole
/// UI re-localizes live, without a restart.
///
/// The chosen language is a GLOBAL (not per-device) preference, persisted in a small settings file
/// next to the device configs so it can be applied at the very first frame — before any device
/// config or dependency injection exists — and thus localizes the splash and setup windows too.
/// Keeping it out of <c>config.json</c> also means no schema change and no new migrator.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    /// <summary>
    /// Languages the app ships, in menu order. Add a language by dropping a <c>&lt;code&gt;.json</c>
    /// and a <c>commands.&lt;code&gt;.json</c> under Localization/Strings and adding it here.
    /// </summary>
    public static readonly IReadOnlyList<LanguageOption> AvailableLanguages =
    [
        new LanguageOption("en", "English")
    ];

    private const string BaseLanguage = "en";
    private const string SettingsFileName = "ui-settings.json";

    private static readonly PropertyChangedEventArgs AllChanged = new(string.Empty);
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    /// <summary>Keys already reported as missing, so a lookup in a binding logs once, not per frame.</summary>
    private readonly HashSet<string> _reportedMissingKeys = new(StringComparer.Ordinal);

    private Dictionary<string, string> _base;
    private Dictionary<string, string> _active;

    // Command catalog: keyed by the ENGLISH source text. The [Command] attribute strings are
    // compile-time constants and cannot carry keys, so they are translated at display time by text.
    private Dictionary<string, string> _commandBase;
    private Dictionary<string, string> _commandActive;

    private string _currentLanguage = BaseLanguage;

    private LocalizationManager()
    {
        _base = LoadDictionary(BaseLanguage);
        _active = _base;
        _commandBase = LoadDictionary("commands." + BaseLanguage);
        _commandActive = _commandBase;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Translated string for <paramref name="key"/>: the active language, falling back to English,
    /// then to the key itself, so a missing translation shows the key instead of blanking the UI.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_active.TryGetValue(key, out string value))
            {
                return value;
            }

            if (_base.TryGetValue(key, out string fallback))
            {
                ReportMissingKey(key, _currentLanguage);
                return fallback;
            }

            ReportMissingKey(key, BaseLanguage);
            return key;
        }
    }

    /// <summary>
    /// Translate free-form English text (command display names, groups and descriptions) at display
    /// time. Falls back to the English text, so untranslated entries and plugin-provided commands
    /// simply show their original wording.
    /// </summary>
    public string TrText(string english)
    {
        if (string.IsNullOrEmpty(english))
        {
            return english;
        }

        if (_commandActive.TryGetValue(english, out string value))
        {
            return value;
        }

        return _commandBase.TryGetValue(english, out string fallback) ? fallback : english;
    }

    /// <summary>
    /// Switch the UI language and refresh every live <see cref="TrExtension"/> binding. Unknown
    /// codes fall back to English. Must be called on the UI thread.
    /// </summary>
    public void SetLanguage(string code)
    {
        string normalized = Normalize(code);
        if (normalized == _currentLanguage)
        {
            return;
        }

        _active = normalized == BaseLanguage ? _base : LoadDictionary(normalized);
        _commandActive = normalized == BaseLanguage ? _commandBase : LoadDictionary("commands." + normalized);
        _currentLanguage = normalized;
        _reportedMissingKeys.Clear();

        ApplyCulture(normalized);
        ReportMissingTranslations();

        // An empty property name means "all properties changed" and refreshes every binding;
        // the indexer notification covers the bindings TrExtension creates.
        PropertyChanged?.Invoke(this, AllChanged);
        PropertyChanged?.Invoke(this, IndexerChanged);
    }

    /// <summary>
    /// Read the persisted language (or the OS UI language on first run) and apply it. Call once, as
    /// early as possible in startup, before the first window is created.
    /// </summary>
    public void InitializeFromSettings()
    {
        string code = ReadPersistedLanguage() ?? CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName;
        string normalized = Normalize(code);

        if (normalized == BaseLanguage)
        {
            // SetLanguage would short-circuit, but the process culture still has to be set.
            ApplyCulture(BaseLanguage);
            ReportMissingTranslations();
            return;
        }

        SetLanguage(normalized);
    }

    /// <summary>Persist the chosen language to the global settings file.</summary>
    public void Persist()
    {
        string path = SettingsPath();

        try
        {
            string json = JsonSerializer.Serialize(
                new UiSettings { Language = _currentLanguage },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Localization] Failed to write '{path}': {ex.Message}");
        }
    }

    private static void ApplyCulture(string code)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            Console.WriteLine($"[Localization] Culture '{code}' is unknown to the runtime, keeping the process culture: {ex.Message}");
        }
    }

    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BaseLanguage;
        }

        // Accept "es", "es-ES", "de_DE" and so on by matching on the primary subtag.
        string primary = code.Split('-', '_')[0].ToLowerInvariant();
        return AvailableLanguages.Any(language => language.Code == primary) ? primary : BaseLanguage;
    }

    private static Dictionary<string, string> LoadDictionary(string name)
    {
        Uri uri = new($"avares://LoupixDeck/Localization/Strings/{name}.json");

        try
        {
            using Stream stream = AssetLoader.Open(uri);
            Dictionary<string, string> dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return dictionary == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(dictionary, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Localization] Failed to load dictionary '{name}': {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void ReportMissingKey(string key, string language)
    {
        if (_reportedMissingKeys.Add(key))
        {
            Console.WriteLine($"[Localization] Missing key '{key}' for language '{language}'.");
        }
    }

    /// <summary>
    /// Development aid: list the keys the base dictionary has but the active language lacks, so a
    /// gap surfaces while working rather than in a screenshot. Compiled out of Release builds.
    /// </summary>
    private void ReportMissingTranslations()
    {
#if DEBUG
        if (_currentLanguage == BaseLanguage)
        {
            return;
        }

        List<string> missing = _base.Keys.Where(key => !_active.ContainsKey(key)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        Console.WriteLine($"[Localization] '{_currentLanguage}' is missing {missing.Count} key(s): {string.Join(", ", missing)}");
#endif
    }

    private static string SettingsPath()
    {
        return Path.Combine(FileDialogHelper.GetConfigDir(), SettingsFileName);
    }

    private static string ReadPersistedLanguage()
    {
        string path = SettingsPath();

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            UiSettings settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path));
            return settings?.Language;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Localization] Failed to read '{path}': {ex.Message}");
            return null;
        }
    }

    private sealed class UiSettings
    {
        public string Language { get; set; }
    }
}

/// <summary>A selectable UI language (code plus display name) for the settings dropdown.</summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
