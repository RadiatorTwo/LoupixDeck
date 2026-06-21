using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public string? CurrentSymbolId { get; set; }

    /// <summary>Set by the picker on confirm; null if the dialog was cancelled.</summary>
    public SymbolDefinition? SelectedSymbol { get; set; }
}

/// <summary>
/// Dialog view model for choosing a symbol from <see cref="SymbolLibrary"/>.
/// Supports text search and category filtering over the curated icon set.
/// </summary>
public partial class SymbolPickerViewModel : DialogViewModelBase<SymbolPickerRequest, DialogResult>
{
    private SymbolPickerRequest _request;

    public ObservableCollection<SymbolDefinition> Symbols { get; } = [];

    public ImmutableArray<string> Categories { get; } = SymbolLibrary.CategoriesWithAll;

    /// <summary>FontFamily used by the View to render the MDI glyphs.</summary>
    public FontFamily SymbolFont { get; } = new(SymbolLibrary.FontUri);

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                ApplyFilter();
        }
    } = string.Empty;

    public string SelectedCategory
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                ApplyFilter();
        }
    } = SymbolLibrary.AllCategoriesKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial SymbolDefinition SelectedSymbol { get; set; }

    public IRelayCommand ConfirmCommand => Relay.Create(ConfirmSelection, () => SelectedSymbol != null);
    public IRelayCommand CancelCommand => Relay.Create(CancelSelection);

    /// <summary>Raised when the dialog should close (after Confirm or Cancel).</summary>
    public event Action CloseRequested;

    public SymbolPickerViewModel()
    {
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
        var search = SearchText?.Trim() ?? string.Empty;

        var filtered = SymbolLibrary.All.Where(s =>
            (SelectedCategory == SymbolLibrary.AllCategoriesKey || s.Category == SelectedCategory) &&
            (search.Length == 0 ||
             s.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             s.Id.Contains(search, StringComparison.OrdinalIgnoreCase)));

        Symbols.Clear();
        foreach (var symbol in filtered)
            Symbols.Add(symbol);

        if (SelectedSymbol != null && !Symbols.Contains(SelectedSymbol))
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
