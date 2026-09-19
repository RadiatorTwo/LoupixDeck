using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// The detail pane: one plugin's manifest header and its generated settings form.
///
/// The form is built from what the plugin declares, not written by hand, so everything here is
/// derived from <see cref="IPluginSettingsPage.SettingsSchema"/> and rebuilt whenever that
/// schema can have changed.
/// </summary>
public sealed partial class PluginDetailViewModel : ViewModelBase
{
    private readonly LoadedPlugin _plugin;
    private IPluginSettingsPage _page;
    private IPluginSettings _settings;

    public PluginDetailViewModel(LoadedPlugin plugin)
    {
        _plugin = plugin;

        PluginManifest manifest = plugin.Manifest;
        _name = manifest?.Name ?? plugin.Directory;
        PluginId = manifest?.Id;
        Author = manifest?.Author;
        Version = manifest?.Version;
        SdkVersion = manifest?.SdkVersion;
        _description = manifest?.Description;
        ProjectUrl = manifest?.ProjectUrl;
        Icon = LoadIcon(plugin);

        BuildForm();

        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Stops following language changes. Called when the page drops this pane.</summary>
    public void Detach()
    {
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    /// <summary>Plugin texts are looked up at display time, so a language switch only has to
    /// re-raise them; rebuilding the form would throw pending edits away.</summary>
    private void OnLanguageChanged(object sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DescriptionText));
        foreach (PluginSettingGroupViewModel group in Groups)
            group.RefreshTexts();
        Connection?.RefreshTexts();
    }

    public LoadedPlugin Plugin => _plugin;

    // ---------- Header ----------

    private readonly string _name;
    private readonly string _description;

    public string Name => LocalizationManager.Instance.TrText(_name, PluginId);

    public string PluginId { get; }

    public string Author { get; }

    public string Version { get; }

    public string SdkVersion { get; }

    public string DescriptionText => LocalizationManager.Instance.TrText(_description, PluginId);

    public string ProjectUrl { get; }

    public Bitmap Icon { get; }

    public bool HasIcon => Icon != null;

    public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

    public bool HasProjectUrl => !string.IsNullOrWhiteSpace(ProjectUrl);

    public bool HasPluginId => !string.IsNullOrWhiteSpace(PluginId);

    /// <summary>Author, version and SDK version from whatever the manifest actually carries -
    /// every field is optional, so a sparse manifest simply shows less.</summary>
    public string SubtitleText
    {
        get
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(Author))
                parts.Add(Loc.Tr("PluginStore_ByAuthor", Author));
            if (!string.IsNullOrWhiteSpace(Version))
                parts.Add($"v{Version}");
            if (!string.IsNullOrWhiteSpace(SdkVersion))
                parts.Add(Loc.Tr("Plugins_SdkVersion", SdkVersion));

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>A user copy that overrides a built-in names the version it replaced, so
    /// resetting to the built-in makes sense before it is pressed.</summary>
    public bool IsBundledOverride => _plugin.BundledFallbackVersion != null;

    public string BundledFallbackText => IsBundledOverride
        ? Loc.Tr("Plugins_OverridesBuiltIn", _plugin.BundledFallbackVersion)
        : null;

    // ---------- Load state ----------

    public bool IsLoaded => _plugin.Status == PluginLoadStatus.Loaded;

    /// <summary>Why a plugin is not running, in its own words when it said so.</summary>
    public string FailureText => IsLoaded
        ? null
        : _plugin.FailureReason ?? InstalledPluginRowViewModel.DescribeStatus(_plugin.Status);

    public bool HasFailure => !IsLoaded;

    // ---------- Generated form ----------

    public ObservableCollection<PluginSettingGroupViewModel> Groups { get; } = [];

    public PluginConnectionViewModel Connection { get; private set; }

    public bool HasConnection => Connection != null;

    /// <summary>True when the plugin is running but declares nothing to configure.</summary>
    public bool HasNoSettings => IsLoaded && Groups.Count == 0 && Connection == null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(UnsavedText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    public partial int DirtyCount { get; set; }

    public bool HasUnsavedChanges => DirtyCount > 0;

    public string UnsavedText => DirtyCount switch
    {
        <= 0 => null,
        1 => Loc.Tr("Plugins_UnsavedOne"),
        _ => Loc.Tr("Plugins_UnsavedMany", DirtyCount)
    };

    public IRelayCommand SaveCommand => field ??= Relay.Create(Save, () => HasUnsavedChanges);

    public IRelayCommand DiscardCommand => field ??= Relay.Create(Discard, () => HasUnsavedChanges);

    public IRelayCommand OpenProjectPageCommand => field ??= Relay.Create(() => OpenUrl(ProjectUrl));

    /// <summary>Throws the pending edits away without asking. Used when the page moves on to
    /// another plugin after the user chose to lose them.</summary>
    public void DiscardChanges() => Discard();

    /// <summary>(Re)builds the groups from the schema the plugin declares right now.</summary>
    private void BuildForm()
    {
        foreach (PluginSettingGroupViewModel group in Groups)
        {
            foreach (PluginSettingRowViewModel row in group.Rows)
                row.PropertyChanged -= OnRowChanged;
        }

        Groups.Clear();
        Connection = null;

        _page = _plugin.Instance as IPluginSettingsPage;
        _settings = _plugin.Host?.Settings;

        if (!IsLoaded || _page == null || _settings == null)
        {
            DirtyCount = 0;
            RaiseFormState();
            return;
        }

        // Settings declared before the first heading have no card of their own; they get an
        // untitled one so nothing is dropped.
        PluginSettingGroupViewModel current = new(null, null, PluginId);

        foreach (PluginSettingDescriptor descriptor in _page.SettingsSchema)
        {
            if (descriptor.Kind == PluginSettingKind.Heading)
            {
                if (current.Rows.Count > 0)
                    Groups.Add(current);

                current = new PluginSettingGroupViewModel(descriptor.Label, descriptor.Description, PluginId);
                continue;
            }

            PluginSettingRowViewModel row = PluginSettingRowViewModel.Create(descriptor, PluginId);
            row.Load(_settings);
            row.PropertyChanged += OnRowChanged;
            current.Rows.Add(row);
        }

        if (current.Rows.Count > 0)
            Groups.Add(current);

        if (_page.SettingsActions is { Count: > 0 } actions)
        {
            PluginConnectionViewModel card = new();
            foreach (PluginSettingAction action in actions)
            {
                card.Actions.Add(new PluginActionRowViewModel(action, card, PluginId,
                    beforeInvoke: () =>
                    {
                        WriteAndSave();
                        return Task.CompletedTask;
                    },
                    afterInvoke: () =>
                    {
                        // The rebuild replaces the card, so the message the action just
                        // produced is carried over to the new one.
                        string carried = card.ResultText;
                        bool failed = card.IsFailure;
                        BuildForm();
                        if (Connection != null)
                        {
                            Connection.ResultText = carried;
                            Connection.IsFailure = failed;
                        }

                        return Task.CompletedTask;
                    }));
            }

            Connection = card;
        }

        DirtyCount = 0;
        RaiseFormState();
    }

    private void RaiseFormState()
    {
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(HasNoSettings));
    }

    private void OnRowChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginSettingRowViewModel.IsDirty))
            RecountDirty();
    }

    private void RecountDirty() =>
        DirtyCount = Groups.Sum(group => group.Rows.Count(row => row.IsDirty));

    private void Save()
    {
        WriteAndSave();
        StatusText = Loc.Tr("Plugins_Saved");
    }

    private void WriteAndSave()
    {
        if (_settings == null || _page == null)
            return;

        foreach (PluginSettingGroupViewModel group in Groups)
        {
            foreach (PluginSettingRowViewModel row in group.Rows)
                row.Write(_settings);
        }

        _settings.Save();
        _page.OnSettingsSaved();
        RecountDirty();
    }

    private void Discard()
    {
        foreach (PluginSettingGroupViewModel group in Groups)
        {
            foreach (PluginSettingRowViewModel row in group.Rows)
                row.Revert();
        }

        RecountDirty();
        StatusText = null;
    }

    /// <summary>UseShellExecute routes the URL through the OS browser on both platforms.</summary>
    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugins] Could not open '{url}': {ex.Message}");
        }
    }

    /// <summary>The manifest's icon, when it names one that is really there. A broken or
    /// unsupported image must never break the page.</summary>
    private static Bitmap LoadIcon(LoadedPlugin plugin)
    {
        string file = plugin.Manifest?.IconFile;
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(plugin.Directory))
            return null;

        string path = Path.Combine(plugin.Directory, file);
        if (!File.Exists(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugins] Icon '{path}' could not be read: {ex.Message}");
            return null;
        }
    }
}
