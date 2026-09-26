using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Mutable parameter/result holder passed into <see cref="AppPickerViewModel"/>, mirroring
/// <see cref="SymbolPickerRequest"/>: the caller creates one, hands it to
/// <see cref="AppPickerViewModel.Initialize"/>, and reads <see cref="SelectedApp"/> after confirm.
/// </summary>
public sealed class AppPickerRequest
{
    /// <summary>Set by the picker on confirm; null if the dialog was cancelled.</summary>
    public InstalledApp SelectedApp { get; set; }
}

/// <summary>One row in the picker. The icon arrives later than the name, so it is observable.</summary>
public partial class AppRowViewModel(InstalledApp app) : ObservableObject
{
    public InstalledApp App { get; } = app;

    public string Name => App.Name;
    public string SourceLabel => App.SourceLabel;
    public bool IsGame => App.IsGame;

    [ObservableProperty]
    public partial Bitmap Icon { get; set; }
}

/// <summary>
/// Dialog view model for choosing an installed application. The list is scanned once by
/// <see cref="IAppDiscoveryService"/> and shared with every other consumer; icons stream in
/// afterwards so the names appear immediately. The applications the user added by hand through
/// <see cref="ICustomAppStore"/> are listed first, and more can be added from here.
/// </summary>
public partial class AppPickerViewModel : DialogViewModelBase<AppPickerRequest, DialogResult>, IAsyncInitViewModel
{
    /// <summary>Decode width for a row icon. The row draws it at 40px; decoding a little larger
    /// keeps it sharp on a scaled display without holding full-size bitmaps.</summary>
    private const int IconWidth = 48;

    private readonly IAppDiscoveryService _discovery;
    private readonly ICustomAppStore _customApps;
    private readonly IAppIconExtractor _icons;
    private readonly CancellationTokenSource _cancellation = new();

    private List<AppRowViewModel> _all = [];
    private AppPickerRequest _request;

    public AppPickerViewModel(IAppDiscoveryService discovery, ICustomAppStore customApps, IAppIconExtractor icons)
    {
        _discovery = discovery;
        _customApps = customApps;
        _icons = icons;
    }

    /// <summary>The filtered rows currently shown.</summary>
    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>Set when there is nothing to show and the reason is worth stating — an
    /// unsupported platform, or a scan that found nothing. Shown instead of an empty list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    public partial string BlockReason { get; set; }

    public bool HasBlockReason => !string.IsNullOrWhiteSpace(BlockReason);

    public bool ShowContent => !IsLoading && !HasBlockReason;

    [ObservableProperty]
    public partial string CountLabel { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial AppRowViewModel SelectedApp { get; set; }

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                ApplyFilter();
        }
    } = string.Empty;

    private IRelayCommand _confirmCommand;
    private IRelayCommand _cancelCommand;

    public IRelayCommand ConfirmCommand => Relay.Ref(ref _confirmCommand, ConfirmSelection, () => SelectedApp != null);
    public IRelayCommand CancelCommand => Relay.Ref(ref _cancelCommand, CancelSelection);

    /// <summary>
    /// Adds a program the scan does not find, the same way the action panel's + button does. The
    /// entry is stored, so it is offered again here and in the panel after a restart.
    /// </summary>
    public IAsyncRelayCommand AddApplicationCommand => field ??= Relay.Create(AddApplicationAsync);

    /// <summary>Raised when the dialog should close (after Confirm or Cancel).</summary>
    public event Action CloseRequested;

    public override void Initialize(AppPickerRequest parameter)
    {
        _request = parameter ?? new AppPickerRequest();
    }

    public async Task InitializeAsync()
    {
        // A platform without discovery still offers whatever the user added by hand.
        if (!_discovery.IsSupported)
        {
            Rebuild([]);
            IsLoading = false;

            if (_all.Count == 0)
                BlockReason = "Listing installed applications is not supported on this system. "
                    + "Add a program with the + button.";
            else
                LoadIcons();

            return;
        }

        IReadOnlyList<InstalledApp> apps;
        try
        {
            apps = await _discovery.GetAppsAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Rebuild(apps);
        IsLoading = false;

        if (_all.Count == 0)
        {
            BlockReason = "No installed applications were found.";
            return;
        }

        LoadIcons();
    }

    /// <summary>
    /// Rebuilds the rows from a scan plus the applications the user added by hand. The added ones
    /// come first, as in the action panel: they are there because the scan missed them.
    /// </summary>
    private void Rebuild(IReadOnlyList<InstalledApp> scanned)
    {
        _all = _customApps.Apps.Concat(scanned).Select(app => new AppRowViewModel(app)).ToList();
        ApplyFilter();
        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        int games = _all.Count(row => row.IsGame);
        CountLabel = games > 0
            ? $"{_all.Count} applications, {games} games"
            : $"{_all.Count} applications";
    }

    private async Task AddApplicationAsync()
    {
        string path = await FileDialogHelper.OpenApplicationDialog(WindowHelper.GetActiveWindow());
        if (string.IsNullOrEmpty(path))
            return;

        InstalledApp added = _customApps.Add(path);

        // Add returns null for a program that is already stored; select its row instead.
        AppRowViewModel row = added == null
            ? _all.FirstOrDefault(existing => string.Equals(existing.App.Target, path, StringComparison.OrdinalIgnoreCase))
            : new AppRowViewModel(added);
        if (row == null)
            return;

        if (added != null)
        {
            _all.Insert(0, row);
            UpdateCountLabel();
        }

        BlockReason = null;
        IsLoading = false;

        // The search could hide the row that was just picked.
        SearchText = string.Empty;
        ApplyFilter();
        SelectedApp = row;

        if (added == null)
            return;

        // One icon, so it is not left blank.
        try
        {
            row.Icon = await _icons.GetThumbnailAsync(added, IconWidth, _cancellation.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppPicker] Icon failed for '{added.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Streams the icons in on a background thread. Fire-and-forget on purpose: the list is usable
    /// without icons, and nothing downstream waits on them. Cancelled when the dialog closes, so
    /// closing the picker does not leave a few hundred extractions running.
    /// </summary>
    private void LoadIcons()
    {
        List<AppRowViewModel> rows = _all;
        CancellationToken token = _cancellation.Token;

        _ = Task.Run(async () =>
        {
            foreach (AppRowViewModel row in rows)
            {
                if (token.IsCancellationRequested)
                    return;

                Bitmap icon;
                try
                {
                    icon = await _icons.GetThumbnailAsync(row.App, IconWidth, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // One unreadable executable must not stop the remaining icons.
                    Console.WriteLine($"[AppPicker] Icon failed for '{row.Name}': {ex.Message}");
                    continue;
                }

                if (icon == null || token.IsCancellationRequested)
                    continue;

                await Dispatcher.UIThread.InvokeAsync(() => row.Icon = icon);
            }
        }, token);
    }

    private void ApplyFilter()
    {
        string search = SearchText?.Trim() ?? string.Empty;

        IEnumerable<AppRowViewModel> filtered = search.Length == 0
            ? _all
            : _all.Where(row =>
                row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.SourceLabel.Contains(search, StringComparison.OrdinalIgnoreCase));

        Apps.Clear();
        foreach (AppRowViewModel row in filtered)
            Apps.Add(row);

        if (SelectedApp != null && !Apps.Contains(SelectedApp))
            SelectedApp = null;
    }

    public void ConfirmSelection()
    {
        if (SelectedApp == null) return;

        _request.SelectedApp = SelectedApp.App;
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelSelection()
    {
        Cancel();
        CloseRequested?.Invoke();
    }

    /// <summary>Stops the icon stream. Called from the View when the window closes, however it was
    /// closed — including the title-bar X, which never reaches Confirm or Cancel.</summary>
    public void StopLoading()
    {
        if (!_cancellation.IsCancellationRequested)
            _cancellation.Cancel();
    }
}