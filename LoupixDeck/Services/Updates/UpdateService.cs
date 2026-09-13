using System.ComponentModel;
using System.Net.Http;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Updates;

public interface IUpdateService : INotifyPropertyChanged
{
    /// <summary>The update the main window hints at; null when there is none or it was skipped.
    /// Changes are raised on the UI thread.</summary>
    UpdateInfo AvailableUpdate { get; }

    /// <summary>Whether the app checks for updates on its own after startup. Persisted.</summary>
    bool AutoCheckEnabled { get; set; }

    /// <summary>Raised on the UI thread when a check finds an update the user has not skipped.</summary>
    event Action<UpdateInfo> UpdateFound;

    /// <summary>
    /// Asks GitHub for the latest stable release. Never throws: failures come back as
    /// <see cref="UpdateCheckStatus.Failed"/> and are logged. A manual check ignores a skipped
    /// version, so the About dialog always reports what is really available.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken = default);

    /// <summary>Checks once in the background after startup, if the automatic check is on.</summary>
    void StartAutomaticCheck();

    /// <summary>Hides the hint for this version until a newer release comes out.</summary>
    void SkipVersion(UpdateInfo update);
}

public sealed partial class UpdateService : ObservableObject, IUpdateService
{
    private const string AutoCheckKey = "CheckForUpdates";
    private const string SkippedVersionKey = "SkippedUpdateVersion";

    /// <summary>Gives startup, devices and plugins time to settle before the network is touched.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    private readonly GitHubReleaseClient _client = new();

    public event Action<UpdateInfo> UpdateFound;

    [ObservableProperty]
    public partial UpdateInfo AvailableUpdate { get; private set; }

    public bool AutoCheckEnabled
    {
        get => UiSettingsStore.GetBool(AutoCheckKey, true);
        set
        {
            if (value == AutoCheckEnabled)
            {
                return;
            }

            UiSettingsStore.Set(AutoCheckKey, value);
            OnPropertyChanged();
        }
    }

    public void StartAutomaticCheck()
    {
        if (!AutoCheckEnabled)
        {
            Console.WriteLine("[Update] Automatic update check is turned off.");
            return;
        }

        if (AppVersion.IsDevelopmentBuild)
        {
            Console.WriteLine($"[Update] Development build ({AppVersion.Text ?? "no version"}) - automatic update check skipped.");
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(StartupDelay);
            await CheckAsync(manual: false);
        });
    }

    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<ReleaseInfo> releases = await _client.GetStableReleasesAsync(cancellationToken);
            Version installed = AppVersion.Current ?? new Version(0, 0, 0);
            List<ReleaseInfo> newer = releases.Where(r => r.Version > installed).ToList();

            if (newer.Count == 0)
            {
                Console.WriteLine($"[Update] Up to date ({AppVersion.Text}).");
                await PublishAsync(null);
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            UpdateInfo update = new(AppVersion.Text, newer[0], newer);
            Console.WriteLine($"[Update] {update.Latest.Tag} is available (installed: {AppVersion.Text}).");

            bool skipped = AppVersion.TryParse(UiSettingsStore.GetString(SkippedVersionKey)) == update.Latest.Version;
            if (skipped && !manual)
            {
                Console.WriteLine($"[Update] {update.Latest.Tag} was skipped by the user.");
                await PublishAsync(null);
            }
            else
            {
                await PublishAsync(update);
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, update);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidOperationException)
        {
            // Offline, timeout, rate limit or an unexpected payload: log only, never a dialog.
            Console.WriteLine($"[Update] Update check failed: {ex.Message}");
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Error: ex.Message);
        }
    }

    public void SkipVersion(UpdateInfo update)
    {
        if (update is null)
        {
            return;
        }

        UiSettingsStore.Set(SkippedVersionKey, update.Latest.Version.ToString());
        Console.WriteLine($"[Update] Skipping {update.Latest.Tag}.");
        Dispatcher.UIThread.Post(() => AvailableUpdate = null);
    }

    private async Task PublishAsync(UpdateInfo update)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            bool isNew = update is not null && AvailableUpdate?.Latest.Version != update.Latest.Version;
            AvailableUpdate = update;
            if (isNew)
            {
                UpdateFound?.Invoke(update);
            }
        });
    }
}
