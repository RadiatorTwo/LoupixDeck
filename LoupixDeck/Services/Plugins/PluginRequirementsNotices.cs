using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Notices;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Turns the unmet requirements of loaded plugins (<see cref="IPluginRequirements"/>, issue #315)
/// into notices in the main window's strip, one per affected plugin. A dismissed notice stays
/// dismissed until the set of unmet requirements of that plugin changes, so the user is not
/// nagged on every start about something they already know.
/// </summary>
public sealed class PluginRequirementsNotices
{
    private const string NoticeIdPrefix = "plugin-requirements:";

    /// <summary><c>ui-settings.json</c> key prefix; the plugin id is appended. The value is the
    /// fingerprint of the unmet requirements the user dismissed.</summary>
    private const string DismissedKeyPrefix = "PluginRequirementsDismissed.";

    private readonly IPluginManager _pluginManager;
    private readonly INoticeService _notices;
    private readonly Action<string> _openPluginDetails;

    // Ids of the notices currently posted, so a plugin that stopped reporting problems (or was
    // unloaded) gets its notice taken down again.
    private readonly HashSet<string> _posted = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <param name="openPluginDetails">Opens the Plugins window on the details of the plugin with
    /// the given id.</param>
    public PluginRequirementsNotices(IPluginManager pluginManager, INoticeService notices,
        Action<string> openPluginDetails)
    {
        _pluginManager = pluginManager;
        _notices = notices;
        _openPluginDetails = openPluginDetails;

        _pluginManager.RequirementsChanged += Refresh;
        Refresh();
    }

    /// <summary>Stable text identifying which requirements of a plugin are unmet.</summary>
    public static string Fingerprint(IEnumerable<PluginRequirement> requirements) =>
        string.Join("|", requirements.Where(r => !r.IsMet).Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal));

    private void Refresh()
    {
        lock (_gate)
        {
            HashSet<string> current = new(StringComparer.Ordinal);

            foreach (LoadedPlugin plugin in _pluginManager.Plugins)
            {
                string pluginId = plugin.Manifest?.Id;
                if (string.IsNullOrEmpty(pluginId))
                    continue;

                List<PluginRequirement> unmet = plugin.Requirements.Where(r => !r.IsMet).ToList();
                string dismissedKey = DismissedKeyPrefix + pluginId;

                if (unmet.Count == 0)
                {
                    // Everything is fine again: forget the dismissal, so a relapse is reported anew.
                    if (UiSettingsStore.GetString(dismissedKey) != null)
                        UiSettingsStore.Set(dismissedKey, null);
                    continue;
                }

                string fingerprint = Fingerprint(unmet);
                if (UiSettingsStore.GetString(dismissedKey) == fingerprint)
                    continue;

                string noticeId = NoticeIdPrefix + pluginId;
                current.Add(noticeId);
                _notices.Post(BuildNotice(noticeId, pluginId, plugin, unmet, dismissedKey, fingerprint));
            }

            foreach (string stale in _posted.Except(current).ToList())
                _notices.Remove(stale);

            _posted.Clear();
            _posted.UnionWith(current);
        }
    }

    private Notice BuildNotice(string noticeId, string pluginId, LoadedPlugin plugin,
        List<PluginRequirement> unmet, string dismissedKey, string fingerprint)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string pluginName = loc.TrText(plugin.Manifest.Name ?? pluginId, pluginId);

        string message;
        if (unmet.Count == 1)
        {
            string text = unmet[0].Message ?? unmet[0].Name;
            message = Loc.Tr("PluginRequirements_NoticeOne", pluginName, loc.TrText(text, pluginId));
        }
        else
        {
            message = Loc.Tr("PluginRequirements_NoticeMany", pluginName, unmet.Count);
        }

        ICommand details = new RelayCommand(() => _openPluginDetails?.Invoke(pluginId));

        return new Notice
        {
            Id = noticeId,
            // mdi-alert-circle-outline
            Icon = char.ConvertFromUtf32(0xF05D6),
            Message = message,
            ActionText = Loc.Tr("Update_Details"),
            ActionCommand = details,
            IsDismissable = true,
            OnDismissed = () => UiSettingsStore.Set(dismissedKey, fingerprint)
        };
    }
}
