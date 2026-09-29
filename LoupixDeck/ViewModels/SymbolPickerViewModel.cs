using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.IconPacks;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
// LoupixDeck.Utils also declares a RelayCommand; the dialog needs the
// CommunityToolkit one (synchronous, supports canExecute).
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Mutable parameter/result holder passed into <see cref="SymbolPickerViewModel"/>.
/// The caller creates one, hands it to <see cref="SymbolPickerViewModel.Initialize"/>,
/// and reads <see cref="SelectedSymbol"/> or <see cref="SelectedPackIcon"/> after the dialog confirms.
/// </summary>
public sealed class SymbolPickerRequest
{
    /// <summary>Symbol id to pre-select when re-picking; null for a fresh pick.</summary>
    public string CurrentSymbolId { get; set; }

    /// <summary>Picker key of the pack icon to pre-select when re-picking (see <see cref="IconPackKey"/>).</summary>
    public string CurrentPackIconKey { get; set; }

    /// <summary>Set by the picker on confirm when a glyph was chosen; null otherwise.</summary>
    public SymbolDefinition SelectedSymbol { get; set; }

    /// <summary>Set by the picker on confirm when an icon from an icon pack was chosen; null otherwise.</summary>
    public IconPackEntry SelectedPackIcon { get; set; }
}

/// <summary>The icon set the picker shows.</summary>
public enum SymbolSource
{
    /// <summary>The curated <see cref="SymbolLibrary.All"/> list (default).</summary>
    Curated,

    /// <summary>Every icon of the bundled Material Design Icons font.</summary>
    MdiAll,

    /// <summary>Every icon of the bundled Material Design Light font.</summary>
    MdiLight,

    /// <summary>A folder of icon files the user added (<see cref="SymbolSourceOption.Pack"/>).</summary>
    IconPack
}

public sealed record SymbolSourceOption(SymbolSource Source, string DisplayName, IconPack Pack = null)
{
    /// <summary>
    /// Stable key persisted in <c>ui-settings.json</c>: the enum name for the built-in sources,
    /// <c>pack:&lt;id&gt;</c> for an icon pack.
    /// </summary>
    public string Key => Pack != null ? $"pack:{Pack.Id}" : Source.ToString();
}

/// <summary>A category filter entry; <see cref="Key"/> is the value compared against symbols.</summary>
public sealed record SymbolCategoryOption(string Key, string DisplayName);

/// <summary>
/// One entry in the picker grid, with its own selection state for the highlight: a font glyph
/// (<see cref="Symbol"/>) or an icon-pack file (<see cref="Icon"/>).
/// </summary>
public sealed partial class SymbolCell : ObservableObject
{
    private readonly IconThumbnailLoader _thumbnails;
    private bool _thumbnailRequested;

    public SymbolCell(SymbolDefinition symbol)
    {
        Symbol = symbol;
        Key = symbol.Id;
        DisplayName = symbol.DisplayName;
    }

    public SymbolCell(IconPackEntry icon, IconThumbnailLoader thumbnails)
    {
        Icon = icon;
        Key = icon.Key;
        DisplayName = icon.DisplayName;
        _thumbnails = thumbnails;
    }

    public SymbolDefinition Symbol { get; }

    public IconPackEntry Icon { get; }

    /// <summary>Unique key of the cell within its source: the symbol id or the pack icon key.</summary>
    public string Key { get; }

    public string DisplayName { get; }

    public bool IsGlyph => Symbol != null;

    /// <summary>
    /// Preview of a pack icon. The whole pack is decoded while the picker is open; a cell the grid
    /// shows before its turn moves to the front of the queue, so visible icons come first.
    /// </summary>
    public Bitmap Thumbnail
    {
        get
        {
            if (field == null && !_thumbnailRequested && _thumbnails != null)
            {
                _thumbnailRequested = true;
                field = _thumbnails.GetOrPrioritize(Icon);
            }

            return field;
        }
        set => SetProperty(ref field, value);
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// One row of the picker grid. The grid is a virtualized list of rows rather than a wrap panel,
/// so only the visible rows are realized even when a source holds thousands of icons.
/// </summary>
public sealed class SymbolRow(ImmutableArray<SymbolCell> cells)
{
    public ImmutableArray<SymbolCell> Cells { get; } = cells;
}

/// <summary>
/// Dialog view model for choosing a symbol. Shows the curated list by default; the full
/// Material Design Icons and Material Design Light sets, and icon packs the user added from
/// folders, are opt-in sources that the picker remembers in <c>ui-settings.json</c>. Supports text
/// search and category filtering.
/// </summary>
public partial class SymbolPickerViewModel : DialogViewModelBase<SymbolPickerRequest, DialogResult>
{
    /// <summary>Delay before memory is handed back, so the closed window's last frame has let go of it.</summary>
    private static readonly TimeSpan MemoryTrimDelay = TimeSpan.FromSeconds(1);

    /// <summary>Symbols per grid row; matches the fixed dialog width.</summary>
    public const int ColumnsPerRow = 6;

    private const string SourceSettingKey = "symbolPickerSource";

    private readonly IIconPackService _iconPacks;
    private SymbolPickerRequest _request;
    private readonly Dictionary<string, SymbolCell> _cellsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _persistSource = true;

    // Icon pack state: the index shown, the running scan, and a key to select once a scan lands.
    private IconPackIndex _packIndex;
    private CancellationTokenSource _scanCancellation;
    private int _scanGeneration;
    private string _pendingSelectKey;
    private IconThumbnailLoader _thumbnails;

    public ObservableCollection<SymbolRow> Rows { get; } = [];

    [ObservableProperty]
    public partial ImmutableArray<SymbolSourceOption> Sources { get; set; }

    [ObservableProperty]
    public partial ImmutableArray<SymbolCategoryOption> Categories { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePackCommand))]
    public partial SymbolSourceOption SelectedSource { get; set; }

    [ObservableProperty]
    public partial SymbolCategoryOption SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>Message shown over the grid for an icon pack that has nothing to show; null hides it.</summary>
    [ObservableProperty]
    public partial string PackStatusText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial SymbolCell SelectedCell { get; set; }

    private IRelayCommand _confirmCommand;
    private IRelayCommand _cancelCommand;
    private IAsyncRelayCommand _addPackCommand;
    private IRelayCommand _removePackCommand;

    public IRelayCommand ConfirmCommand => Relay.Ref(ref _confirmCommand, ConfirmSelection, () => SelectedCell != null);
    public IRelayCommand CancelCommand => Relay.Ref(ref _cancelCommand, CancelSelection);
    public IAsyncRelayCommand AddPackCommand => Relay.Ref(ref _addPackCommand, AddPackAsync);
    public IRelayCommand RemovePackCommand => Relay.Ref(ref _removePackCommand, RemovePack, () => SelectedSource?.Pack != null);

    /// <summary>Raised when the dialog should close (after Confirm or Cancel).</summary>
    public event Action CloseRequested;

    /// <summary>Raised with a row index when a selection landed after the window opened (async pack scan).</summary>
    public event Action<int> ScrollToRowRequested;

    /// <summary>
    /// Row of the symbol pre-selected by <see cref="Initialize"/>, or -1. Initialize runs before the
    /// window exists, so the view scrolls to it once it has opened.
    /// </summary>
    public int InitialRowIndex { get; private set; } = -1;

    public SymbolPickerViewModel(IIconPackService iconPacks)
    {
        _iconPacks = iconPacks;

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        Sources = BuildSources();
        string saved = UiSettingsStore.GetString(SourceSettingKey);

        // A remembered pack that was removed since falls back to the curated list.
        _persistSource = false;
        SelectedSource = Sources.FirstOrDefault(o => o.Key == saved) ?? Sources[0];
        _persistSource = true;
    }

    /// <summary>
    /// Applies the request. The picker always opens in the remembered source, the same for every
    /// device; the current symbol or pack icon is only pre-selected when that source contains it.
    /// </summary>
    public override void Initialize(SymbolPickerRequest parameter)
    {
        _request = parameter ?? new SymbolPickerRequest();

        if (IconPackKey.TryParse(_request.CurrentPackIconKey, out string packId, out _))
        {
            // A pack is scanned asynchronously; the icon is selected once the scan has landed.
            if (SelectedSource.Pack?.Id == packId)
            {
                _pendingSelectKey = _request.CurrentPackIconKey;
                TrySelectPending(scroll: false);
            }

            return;
        }

        if (!string.IsNullOrEmpty(_request.CurrentSymbolId))
            Select(_request.CurrentSymbolId);
    }

    private ImmutableArray<SymbolSourceOption> BuildSources()
    {
        return
        [
            new(SymbolSource.Curated, Loc.Tr("SymbolPicker_SourceCurated")),
            new(SymbolSource.MdiAll, Loc.Tr("SymbolPicker_SourceMdiAll")),
            new(SymbolSource.MdiLight, Loc.Tr("SymbolPicker_SourceMdiLight")),
            .. _iconPacks.Packs.Select(static p => new SymbolSourceOption(
                SymbolSource.IconPack,
                Directory.Exists(p.Path) ? p.Name : Loc.Tr("SymbolPicker_PackMissingFmt", p.Name),
                p))
        ];
    }

    partial void OnSelectedSourceChanged(SymbolSourceOption value)
    {
        if (value == null) return;

        if (_persistSource)
            UiSettingsStore.Set(SourceSettingKey, value.Key);

        CancelScan();
        PackStatusText = null;
        _packIndex = null;
        // Stop decoding the previous pack; what is decoded stays until the picker closes.
        _thumbnails?.LoadAll([]);

        if (value.Pack != null)
        {
            LoadPack(value.Pack);
            return;
        }

        IEnumerable<string> keys = value.Source switch
        {
            SymbolSource.MdiAll => SymbolLibrary.FullCategories(SymbolFontLibrary.Mdi),
            SymbolSource.MdiLight => SymbolLibrary.FullCategories(SymbolFontLibrary.MdiLight),
            _ => SymbolLibrary.Categories
        };

        Categories = [AllCategory(),
            .. keys.Select(static k => new SymbolCategoryOption(
                k, k == SymbolLibrary.OtherCategoryKey ? Loc.Tr("SymbolPicker_OtherCategory") : k))];

        // Setting the category runs the filter.
        SelectedCategory = null;
        SelectedCategory = Categories[0];
    }

    private static SymbolCategoryOption AllCategory()
        => new(SymbolLibrary.AllCategoriesKey, Loc.Tr("SymbolPicker_AllCategories"));

    /// <summary>
    /// Shows a pack: its last scan at once when there is one, then a fresh scan, so files added to the
    /// folder since appear without restarting.
    /// </summary>
    private async void LoadPack(IconPack pack)
    {
        int generation = ++_scanGeneration;
        CancellationTokenSource cancellation = new();
        _scanCancellation = cancellation;

        IconPackIndex cached = _iconPacks.GetCachedIndex(pack.Id);
        if (cached != null)
            ApplyPackIndex(cached, keepCategory: false);
        else
        {
            Categories = [AllCategory()];
            SelectedCategory = null;
            SelectedCategory = Categories[0];
            PackStatusText = Loc.Tr("SymbolPicker_PackScanning");
        }

        IconPackIndex index;
        try
        {
            index = await _iconPacks.ScanAsync(pack, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IconPacks] Scanning '{pack.Path}' failed: {ex.Message}");
            index = IconPackIndex.Missing(pack.Id);
        }

        if (generation != _scanGeneration)
            return;

        // An unchanged folder keeps the grid (and its scroll position) as it is.
        if (cached == null || !SameEntries(cached, index))
            ApplyPackIndex(index, keepCategory: cached != null);

        TrySelectPending(scroll: true);
        _pendingSelectKey = null;
    }

    private static bool SameEntries(IconPackIndex a, IconPackIndex b)
    {
        return a.FolderMissing == b.FolderMissing && a.Entries.Length == b.Entries.Length &&
               a.Entries.Select(static e => e.Key).SequenceEqual(b.Entries.Select(static e => e.Key));
    }

    private void ApplyPackIndex(IconPackIndex index, bool keepCategory)
    {
        _packIndex = index;
        string previous = keepCategory ? SelectedCategory?.Key : null;

        Categories = [AllCategory(),
            .. index.Categories.Select(static k => new SymbolCategoryOption(
                k, k == IconPackScanner.RootCategoryKey ? Loc.Tr("SymbolPicker_PackRootCategory") : k))];

        PackStatusText = index.FolderMissing ? Loc.Tr("SymbolPicker_PackMissingInfo")
            : !index.Entries.IsEmpty ? null
            : index.SkippedFontFiles > 0 ? Loc.Tr("SymbolPicker_PackFontsUnsupported")
            : Loc.Tr("SymbolPicker_PackEmpty");

        // Queue the whole pack before the grid is rebuilt, so the cells it shows first can jump the queue.
        if (!index.Entries.IsEmpty)
            Thumbnails().LoadAll(index.Entries);

        SelectedCategory = null;
        SelectedCategory = Categories.FirstOrDefault(c => c.Key == previous) ?? Categories[0];
        TrySelectPending(scroll: false);
    }

    /// <summary>The session's thumbnail loader, created when the first pack is shown.</summary>
    private IconThumbnailLoader Thumbnails()
    {
        if (_thumbnails != null)
            return _thumbnails;

        _thumbnails = new IconThumbnailLoader(ThumbnailColor());
        _thumbnails.ThumbnailsLoaded += OnThumbnailsLoaded;
        return _thumbnails;
    }

    private void OnThumbnailsLoaded(IReadOnlyList<(string Key, Bitmap Bitmap)> batch)
    {
        foreach ((string key, Bitmap bitmap) in batch)
        {
            if (_cellsById.TryGetValue(key, out SymbolCell cell))
                cell.Thumbnail = bitmap;
        }
    }

    private void TrySelectPending(bool scroll)
    {
        if (_pendingSelectKey == null || !_cellsById.ContainsKey(_pendingSelectKey))
            return;

        Select(_pendingSelectKey);
        _pendingSelectKey = null;

        if (scroll && InitialRowIndex >= 0)
            ScrollToRowRequested?.Invoke(InitialRowIndex);
    }

    private void CancelScan()
    {
        _scanGeneration++;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }

    private async Task AddPackAsync()
    {
        string path = await FileDialogHelper.OpenFolderDialog(Loc.Tr("SymbolPicker_SelectPackFolder"));
        if (string.IsNullOrEmpty(path))
            return;

        IconPack pack = _iconPacks.Add(path);
        if (pack == null)
            return;

        Sources = BuildSources();
        SelectedSource = Sources.First(o => o.Pack?.Id == pack.Id);
    }

    /// <summary>
    /// Removes the selected pack from the picker. Only the reference goes: the folder stays on disk,
    /// and buttons using its icons keep working because each placed icon was copied to the assets.
    /// </summary>
    private void RemovePack()
    {
        if (SelectedSource?.Pack is not { } pack)
            return;

        _iconPacks.Remove(pack.Id);
        Sources = BuildSources();
        SelectedSource = Sources[0];
    }

    partial void OnSelectedCategoryChanged(SymbolCategoryOption value)
    {
        if (value != null)
            ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedCellChanged(SymbolCell value)
    {
        foreach (SymbolCell cell in _cellsById.Values)
            cell.IsSelected = value != null && cell.Key.Equals(value.Key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Selects a tapped grid cell.</summary>
    public void SelectCell(SymbolCell cell)
    {
        SelectedCell = cell;
    }

    private void Select(string key)
    {
        if (!_cellsById.TryGetValue(key, out SymbolCell cell))
            return;

        SelectedCell = cell;
        InitialRowIndex = Rows.IndexOf(Rows.First(r => r.Cells.Contains(cell)));
    }

    private ImmutableArray<SymbolDefinition> SourceSymbols() => SelectedSource?.Source switch
    {
        SymbolSource.MdiAll => SymbolLibrary.FullIcons(SymbolFontLibrary.Mdi),
        SymbolSource.MdiLight => SymbolLibrary.FullIcons(SymbolFontLibrary.MdiLight),
        _ => SymbolLibrary.All
    };

    private void ApplyFilter()
    {
        _searchTimer.Stop();

        string search = SearchText?.Trim() ?? string.Empty;
        string category = SelectedCategory?.Key ?? SymbolLibrary.AllCategoriesKey;

        List<SymbolCell> filtered = SelectedSource?.Pack != null ? FilterPackIcons(search, category) : FilterSymbols(search, category);

        _cellsById.Clear();
        Rows.Clear();

        for (int i = 0; i < filtered.Count; i += ColumnsPerRow)
        {
            ImmutableArray<SymbolCell> cells = [.. filtered.Skip(i).Take(ColumnsPerRow)];
            foreach (SymbolCell cell in cells)
                _cellsById.TryAdd(cell.Key, cell);
            Rows.Add(new SymbolRow(cells));
        }

        CountText = _packIndex is { Truncated: true } && SelectedSource?.Pack != null
            ? Loc.Tr("SymbolPicker_PackTruncatedFmt", filtered.Count, IconPackScanner.MaxEntries)
            : Loc.Tr("SymbolPicker_CountFmt", filtered.Count);

        // Cells are rebuilt, so re-point the selection at the new cell with the same key.
        if (SelectedCell != null && _cellsById.TryGetValue(SelectedCell.Key, out SymbolCell selected))
        {
            SelectedCell = selected;
            selected.IsSelected = true;
        }
        else
            SelectedCell = null;
    }

    private List<SymbolCell> FilterSymbols(string search, string category)
    {
        bool curated = SelectedSource?.Source is null or SymbolSource.Curated;

        return SourceSymbols().Where(s =>
                (category == SymbolLibrary.AllCategoriesKey || InCategory(s, category, curated)) &&
                (search.Length == 0 || Matches(s, search)))
            .Select(static s => new SymbolCell(s))
            .ToList();
    }

    /// <summary>
    /// Filters the pack's icons. Every word of the search must appear in the icon's search text (file
    /// name and its parts, folder, tags), so "arrow left" finds "arrow-left-bold".
    /// </summary>
    private List<SymbolCell> FilterPackIcons(string search, string category)
    {
        if (_packIndex == null)
            return [];

        IconThumbnailLoader thumbnails = Thumbnails();

        string[] words = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return _packIndex.Entries.Where(e =>
                (category == SymbolLibrary.AllCategoriesKey || e.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)) &&
                words.All(w => e.SearchText.Contains(w, StringComparison.Ordinal)))
            .Select(e => new SymbolCell(e, thumbnails))
            .ToList();
    }

    /// <summary>The theme's text color, which single-color pack icons are previewed in.</summary>
    private static Color ThumbnailColor()
    {
        if (Application.Current is { } app &&
            app.TryGetResource("AppTextPrimary", app.ActualThemeVariant, out object resource) &&
            resource is ISolidColorBrush brush)
            return brush.Color;

        return Colors.Gray;
    }

    private static bool InCategory(SymbolDefinition symbol, string category, bool curated)
    {
        if (curated)
            return symbol.Category == category;

        return category == SymbolLibrary.OtherCategoryKey ? symbol.Tags.IsEmpty : symbol.Tags.Contains(category);
    }

    private static bool Matches(SymbolDefinition symbol, string search)
    {
        return symbol.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               symbol.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               symbol.Aliases.Any(a => a.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
               symbol.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    public void ConfirmSelection()
    {
        if (SelectedCell == null) return;

        _request.SelectedSymbol = SelectedCell.Symbol;
        _request.SelectedPackIcon = SelectedCell.Icon;
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelSelection()
    {
        Cancel();
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Stops a running scan and the thumbnail workers once the dialog has closed, and frees the
    /// thumbnails: the picker's memory is only in use while it is open. After a pack was shown, the
    /// freed memory is also handed back to the OS, since the app then idles in the background.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="IDisposable"/>: the view model is a transient of the device
    /// container, which would keep every disposable instance alive until the device goes away.
    /// </remarks>
    public void ReleaseResources()
    {
        _searchTimer.Stop();
        CancelScan();

        bool hadThumbnails = _thumbnails != null;
        if (hadThumbnails)
        {
            _thumbnails.ThumbnailsLoaded -= OnThumbnailsLoaded;
            _thumbnails.Dispose();
            _thumbnails = null;
        }

        _cellsById.Clear();
        Rows.Clear();
        _packIndex = null;
        SelectedCell = null;

        if (hadThumbnails)
            DispatcherTimer.RunOnce(MemoryTrim.ReturnFreedMemory, MemoryTrimDelay, DispatcherPriority.Background);
    }
}
