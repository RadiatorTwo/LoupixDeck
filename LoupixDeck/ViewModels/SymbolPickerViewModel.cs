using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
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
public class SymbolPickerRequest
{
    /// <summary>Symbol id to pre-select when re-picking; null for a fresh pick.</summary>
    public string CurrentSymbolId { get; set; }

    /// <summary>Set by the picker on confirm; null if the dialog was cancelled.</summary>
    public SymbolDefinition SelectedSymbol { get; set; }
}

/// <summary>
/// Dialog view model for choosing a symbol from <see cref="SymbolLibrary"/>.
/// Supports text search and category filtering over the curated icon set.
/// </summary>
public class SymbolPickerViewModel : DialogViewModelBase<SymbolPickerRequest, DialogResult>
{
    private const string AllCategories = "All";

    private SymbolPickerRequest _request;

    public ObservableCollection<SymbolDefinition> Symbols { get; } = [];

    public IReadOnlyList<string> Categories { get; } =
        new[] { AllCategories }.Concat(SymbolLibrary.Categories).ToArray();

    /// <summary>FontFamily used by the View to render the MDI glyphs.</summary>
    public FontFamily SymbolFont { get; } = new(SymbolLibrary.FontUri);

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private string _selectedCategory = AllCategories;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                ApplyFilter();
        }
    }

    private SymbolDefinition _selectedSymbol;
    public SymbolDefinition SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            if (SetProperty(ref _selectedSymbol, value))
                ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
        }
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Raised when the dialog should close (after Confirm or Cancel).</summary>
    public event Action CloseRequested;

    public SymbolPickerViewModel()
    {
        ConfirmCommand = new RelayCommand(ConfirmSelection, () => SelectedSymbol != null);
        CancelCommand = new RelayCommand(CancelSelection);
        ApplyFilter();
    }

    public override void Initialize(SymbolPickerRequest parameter)
    {
        _request = parameter ?? new SymbolPickerRequest();

        if (!string.IsNullOrEmpty(_request.CurrentSymbolId) &&
            SymbolLibrary.TryGet(_request.CurrentSymbolId, out var current))
        {
            SelectedCategory = current.Category;
            SelectedSymbol = Symbols.FirstOrDefault(s => s.Id == current.Id);
        }
    }

    private void ApplyFilter()
    {
        var search = _searchText?.Trim() ?? string.Empty;

        var filtered = SymbolLibrary.All.Where(s =>
            (_selectedCategory == AllCategories || s.Category == _selectedCategory) &&
            (search.Length == 0 ||
             s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             s.Id.Contains(search, StringComparison.OrdinalIgnoreCase)));

        Symbols.Clear();
        foreach (var symbol in filtered)
            Symbols.Add(symbol);

        if (_selectedSymbol != null && !Symbols.Contains(_selectedSymbol))
            SelectedSymbol = null;
    }

    public void ConfirmSelection()
    {
        if (SelectedSymbol == null) return;

        _request.SelectedSymbol = SelectedSymbol;
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelSelection()
    {
        Cancel();
        CloseRequested?.Invoke();
    }
}
