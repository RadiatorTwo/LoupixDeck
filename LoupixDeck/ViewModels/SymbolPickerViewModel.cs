using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
// LoupixDeck.Utils also declares a RelayCommand; the dialog needs the
// CommunityToolkit one (synchronous, supports canExecute).
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Mutable parameter/result holder passed into <see cref="SymbolPickerViewModel"/>.
/// The caller creates one, hands it to <see cref="SymbolPickerViewModel.Initialize"/>,
/// and reads <see cref="SelectedSymbol"/> after the dialog confirms.
/// </summary>
public sealed class SymbolPickerRequest
{
    /// <summary>Symbol id to pre-select when re-picking; null for a fresh pick.</summary>
    public string CurrentSymbolId { get; set; }

    /// <summary>Set by the picker on confirm; null if the dialog was cancelled.</summary>
    public SymbolDefinition SelectedSymbol { get; set; }
}

/// <summary>The icon set the picker shows.</summary>
public enum SymbolSource
{
    /// <summary>The curated <see cref="SymbolLibrary.All"/> list (default).</summary>
    Curated,

    /// <summary>Every icon of the bundled Material Design Icons font.</summary>
    MdiAll,

    /// <summary>Every icon of the bundled Material Design Light font.</summary>
    MdiLight
}

public sealed record SymbolSourceOption(SymbolSource Source, string DisplayName)
{
    /// <summary>Stable key persisted in <c>ui-settings.json</c>; the enum name for the built-in sources.</summary>
    public string Key => Source.ToString();
}

/// <summary>A category filter entry; <see cref="Key"/> is the value compared against symbols.</summary>
public sealed record SymbolCategoryOption(string Key, string DisplayName);

/// <summary>One symbol in the picker grid, with its own selection state for the highlight.</summary>
public sealed partial class SymbolCell(SymbolDefinition symbol) : ObservableObject
{
    public SymbolDefinition Symbol { get; } = symbol;

    /// <summary>Unique key of the cell within its source; the symbol id for glyphs.</summary>
    public string Key { get; } = symbol.Id;

    public string DisplayName { get; } = symbol.DisplayName;

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
/// Material Design Icons and Material Design Light sets are an opt-in source that the picker
/// remembers in <c>ui-settings.json</c>. Supports text search and category filtering.
/// </summary>
public partial class SymbolPickerViewModel : DialogViewModelBase<SymbolPickerRequest, DialogResult>
{
    /// <summary>Symbols per grid row; matches the fixed dialog width.</summary>
    public const int ColumnsPerRow = 6;

    private const string SourceSettingKey = "symbolPickerSource";

    private SymbolPickerRequest _request;
    private readonly Dictionary<string, SymbolCell> _cellsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _persistSource = true;

    public ObservableCollection<SymbolRow> Rows { get; } = [];

    [ObservableProperty]
    public partial ImmutableArray<SymbolSourceOption> Sources { get; set; } =
    [
        new(SymbolSource.Curated, Loc.Tr("SymbolPicker_SourceCurated")),
        new(SymbolSource.MdiAll, Loc.Tr("SymbolPicker_SourceMdiAll")),
        new(SymbolSource.MdiLight, Loc.Tr("SymbolPicker_SourceMdiLight"))
    ];

    [ObservableProperty]
    public partial ImmutableArray<SymbolCategoryOption> Categories { get; set; }

    [ObservableProperty]
    public partial SymbolSourceOption SelectedSource { get; set; }

    [ObservableProperty]
    public partial SymbolCategoryOption SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CountText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial SymbolCell SelectedCell { get; set; }

    private IRelayCommand _confirmCommand;
    private IRelayCommand _cancelCommand;

    public IRelayCommand ConfirmCommand => Relay.Ref(ref _confirmCommand, ConfirmSelection, () => SelectedCell != null);
    public IRelayCommand CancelCommand => Relay.Ref(ref _cancelCommand, CancelSelection);

    /// <summary>Raised when the dialog should close (after Confirm or Cancel).</summary>
    public event Action CloseRequested;

    /// <summary>
    /// Row of the symbol pre-selected by <see cref="Initialize"/>, or -1. Initialize runs before the
    /// window exists, so the view scrolls to it once it has opened.
    /// </summary>
    public int InitialRowIndex { get; private set; } = -1;

    public SymbolPickerViewModel()
    {
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        string saved = UiSettingsStore.GetString(SourceSettingKey);

        _persistSource = false;
        SelectedSource = Sources.FirstOrDefault(o => o.Key == saved) ?? Sources[0];
        _persistSource = true;
    }

    public override void Initialize(SymbolPickerRequest parameter)
    {
        _request = parameter ?? new SymbolPickerRequest();

        if (string.IsNullOrEmpty(_request.CurrentSymbolId) ||
            !SymbolLibrary.TryGet(_request.CurrentSymbolId, out SymbolDefinition current))
            return;

        // Open in a source that contains the current symbol, without changing the remembered one.
        SymbolSource needed = current.Library == SymbolFontLibrary.MdiLight ? SymbolSource.MdiLight
            : SelectedSource.Source == SymbolSource.MdiAll || !SymbolLibrary.All.Contains(current) ? SymbolSource.MdiAll
            : SymbolSource.Curated;

        if (needed != SelectedSource.Source)
        {
            _persistSource = false;
            SelectedSource = Sources.First(o => o.Source == needed);
            _persistSource = true;
        }

        if (needed == SymbolSource.Curated)
            SelectedCategory = Categories.FirstOrDefault(c => c.Key == current.Category) ?? SelectedCategory;

        Select(current.Id);
    }

    partial void OnSelectedSourceChanged(SymbolSourceOption value)
    {
        if (value == null) return;

        if (_persistSource)
            UiSettingsStore.Set(SourceSettingKey, value.Key);

        IEnumerable<string> keys = value.Source switch
        {
            SymbolSource.MdiAll => SymbolLibrary.FullCategories(SymbolFontLibrary.Mdi),
            SymbolSource.MdiLight => SymbolLibrary.FullCategories(SymbolFontLibrary.MdiLight),
            _ => SymbolLibrary.Categories
        };

        Categories = [new(SymbolLibrary.AllCategoriesKey, Loc.Tr("SymbolPicker_AllCategories")),
            .. keys.Select(static k => new SymbolCategoryOption(
                k, k == SymbolLibrary.OtherCategoryKey ? Loc.Tr("SymbolPicker_OtherCategory") : k))];

        // Setting the category runs the filter.
        SelectedCategory = null;
        SelectedCategory = Categories[0];
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
        bool curated = SelectedSource?.Source is null or SymbolSource.Curated;

        List<SymbolDefinition> filtered = SourceSymbols().Where(s =>
            (category == SymbolLibrary.AllCategoriesKey || InCategory(s, category, curated)) &&
            (search.Length == 0 || Matches(s, search))).ToList();

        _cellsById.Clear();
        Rows.Clear();

        for (int i = 0; i < filtered.Count; i += ColumnsPerRow)
        {
            ImmutableArray<SymbolCell> cells = [.. filtered.Skip(i).Take(ColumnsPerRow).Select(s => new SymbolCell(s))];
            foreach (SymbolCell cell in cells)
                _cellsById.TryAdd(cell.Key, cell);
            Rows.Add(new SymbolRow(cells));
        }

        CountText = Loc.Tr("SymbolPicker_CountFmt", filtered.Count);

        // Cells are rebuilt, so re-point the selection at the new cell with the same key.
        if (SelectedCell != null && _cellsById.TryGetValue(SelectedCell.Key, out SymbolCell selected))
        {
            SelectedCell = selected;
            selected.IsSelected = true;
        }
        else
            SelectedCell = null;
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
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelSelection()
    {
        Cancel();
        CloseRequested?.Invoke();
    }
}
