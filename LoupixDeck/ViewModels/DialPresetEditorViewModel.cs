using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.DialPresets;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Names a dial preset — a new one taken from a dial, or an existing one being renamed. The three
/// commands are shown but not edited here: a preset is captured from a configured dial, so changing
/// what it does means configuring a dial and saving it again, in the editor that already exists for
/// exactly that.
/// </summary>
public sealed partial class DialPresetEditorViewModel(IDialPresetStore store)
    : DialogViewModelBase<DialPreset, DialogResult>
{
    /// <summary>The preset being edited. Its name is written back in place, which is how the caller
    /// reads the result once the dialog confirms.</summary>
    public DialPreset Preset { get; private set; }

    public override void Initialize(DialPreset parameter)
    {
        Preset = parameter;
        Name = parameter?.Name ?? string.Empty;

        OnPropertyChanged(nameof(Gestures));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>True when the preset already exists, which only changes the window's wording.</summary>
    public bool IsExisting => Preset != null && store.Presets.Any(p => p.Id == Preset.Id);

    public string Title => IsExisting ? "Rename preset" : "Save dial as preset";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Why the name cannot be used, or null when it can. Shown under the box so the Save
    /// button is never merely disabled without saying why.</summary>
    public string NameError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return null;

            return store.IsNameValid(Name, Preset) ? null : "A preset with this name already exists.";
        }
    }

    public bool HasNameError => NameError != null;

    /// <summary>What the preset binds, one line per gesture it fills.</summary>
    public IReadOnlyList<string> Gestures
    {
        get
        {
            if (Preset == null)
                return [];

            List<string> lines = [];
            foreach ((RotaryAction action, string command) in Preset.ToRotaryGroup())
                lines.Add($"{action.DisplayName()}:  {Describe(command)}");

            return lines;
        }
    }

    /// <summary>A command string as one readable line: the names of its steps, without the
    /// parameter noise that would make a chain unreadable at a glance.</summary>
    private static string Describe(string command) =>
        string.Join(" → ", CommandStringParser.SplitChain(command).Select(CommandStringParser.GetName));

    /// <summary>Raised when the dialog should close, after the result is set. Setting the result
    /// alone leaves the window on screen — the dialog service waits for the window to close.</summary>
    public event Action CloseWindow;

    public IRelayCommand SaveCommand => field ??= Relay.Create(Save, CanSave);

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name) && !HasNameError;

    private void Save()
    {
        if (!CanSave())
            return;

        Preset.Name = Name.Trim();
        Confirm(Models.DialogResult.Ok());
        CloseWindow?.Invoke();
    }

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });
}
