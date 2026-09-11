using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>What the capture dialog is allowed to record.</summary>
public enum KeyCaptureMode
{
    /// <summary>One chord that must contain a real key, e.g. "Ctrl+Shift+S".</summary>
    Combination,

    /// <summary>Several chords played one after another, joined with ", ".</summary>
    Sequence,

    /// <summary>Modifier keys only, e.g. "Ctrl+Shift" — the keys held during a mouse chord.</summary>
    Modifiers
}

/// <summary>
/// Mutable parameter/result holder passed into <see cref="KeyCaptureViewModel"/>. The caller
/// creates one, hands it to <see cref="KeyCaptureViewModel.Initialize"/> and reads
/// <see cref="CapturedKeys"/> after the dialog confirms.
/// </summary>
public sealed class KeyCaptureRequest
{
    /// <summary>What the dialog may record.</summary>
    public KeyCaptureMode Mode { get; set; } = KeyCaptureMode.Combination;

    /// <summary>Set by the dialog on confirm; null when it was cancelled.</summary>
    public string CapturedKeys { get; set; }
}

/// <summary>
/// Records a key combination by pressing it instead of typing its token names: the user holds
/// e.g. Ctrl+Shift+S and the dialog captures "Ctrl+Shift+S", exactly as the keyboard commands
/// expect it. In <see cref="KeyCaptureMode.Sequence"/> it records several chords in a row.
/// </summary>
public sealed class KeyCaptureViewModel : DialogViewModelBase<KeyCaptureRequest, DialogResult>
{
    private KeyCaptureRequest _request;

    /// <summary>Keys currently held, and the order in which they went down.</summary>
    private readonly HashSet<Key> _held = [];
    private readonly List<Key> _order = [];

    /// <summary>Chords already locked in — sequence mode only.</summary>
    private readonly List<string> _steps = [];

    /// <summary>Valid combination of the chord being held, or null while it is incomplete.</summary>
    private string _chord;

    /// <summary>True between the first key going down and the last one coming up.</summary>
    private bool _building;

    /// <summary>The last completed chord — combination and modifier mode only.</summary>
    private string _captured;

    public string DialogTitle => IsSequence ? "Record Key Sequence" : "Record Key Combination";

    public string Prompt => IsSequence
        ? "Press the key combinations one after another. They replay in the order recorded."
        : Mode == KeyCaptureMode.Modifiers
            ? "Hold the modifier keys to record, then release them."
            : "Press the key combination to record.";

    public KeyCaptureMode Mode { get; private set; } = KeyCaptureMode.Combination;

    public bool IsSequence => Mode == KeyCaptureMode.Sequence;

    /// <summary>What the dialog shows: the locked chords plus the one currently being held.</summary>
    public string DisplayText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "…";

    public bool CanConfirm
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
        }
    }

    public bool CanUndo
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                ((RelayCommand)UndoCommand).NotifyCanExecuteChanged();
        }
    }

    public ICommand UndoCommand => field ??= new RelayCommand(() =>
    {
        if (_steps.Count > 0)
            _steps.RemoveAt(_steps.Count - 1);
        Refresh();
    }, () => CanUndo);

    public ICommand ConfirmCommand => field ??= new RelayCommand(() =>
    {
        _request.CapturedKeys = IsSequence ? string.Join(", ", _steps) : _captured;
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }, () => CanConfirm);

    public ICommand CancelCommand => field ??= new RelayCommand(() =>
    {
        Cancel();
        CloseRequested?.Invoke();
    });

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseRequested;

    public override void Initialize(KeyCaptureRequest parameter)
    {
        _request = parameter ?? new KeyCaptureRequest();
        Mode = _request.Mode;

        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsSequence));
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(Prompt));
        Refresh();
    }

    /// <summary>
    /// Called by the view for every key that goes down while the dialog is open. Keys accumulate
    /// in press order so a chord may hold several of them, and auto-repeat is ignored.
    /// </summary>
    public void HandleKeyDown(Key key)
    {
        if (!_building)
        {
            _held.Clear();
            _order.Clear();
            _chord = null;
            _building = true;
        }

        if (_held.Add(key) && !_order.Contains(key))
            _order.Add(key);

        string combo = KeyComboFormatter.BuildCombo(_order, Mode == KeyCaptureMode.Modifiers);
        if (combo != null)
        {
            _chord = combo;

            // Outside sequence mode the chord is locked as it is held, so releasing the keys
            // leaves the last valid combination on screen instead of clearing it.
            if (!IsSequence)
                _captured = combo;
        }

        Refresh();
    }

    /// <summary>Called by the view for every key release; the chord is locked once all keys are up.</summary>
    public void HandleKeyUp(Key key)
    {
        _held.Remove(key);

        if (_held.Count == 0)
        {
            if (IsSequence && _chord != null)
                _steps.Add(_chord);

            _building = false;
            _chord = null;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (IsSequence)
        {
            string shown = string.Join(", ", _steps);
            string partial = _building ? (_chord ?? KeyComboFormatter.PartialText(_order)) : null;

            if (!string.IsNullOrEmpty(partial))
                shown = shown.Length == 0 ? partial + "…" : shown + ", " + partial + "…";

            DisplayText = string.IsNullOrEmpty(shown) ? "…" : shown;
            CanConfirm = _steps.Count > 0;
            CanUndo = _steps.Count > 0;
        }
        else
        {
            string partial = _building ? (_chord ?? KeyComboFormatter.PartialText(_order)) : null;
            DisplayText = !string.IsNullOrEmpty(partial) ? partial : (_captured ?? "…");
            CanConfirm = !string.IsNullOrEmpty(_captured);
            CanUndo = false;
        }
    }
}
