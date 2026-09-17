using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>One key/value line of the detail pane.</summary>
/// <param name="Key">Localized label, shown upper-case.</param>
/// <param name="Value">The value, shown monospaced.</param>
public sealed record DiagnosticFact(string Key, string Value);

/// <summary>
/// One check: a row in the middle column and, when selected, the content of the detail pane.
/// </summary>
public sealed partial class DiagnosticCheckRowViewModel : ViewModelBase
{
    public DiagnosticCheckRowViewModel(DiagnosticCheckResult result)
    {
        Result = result;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(BadgeFill))]
    [NotifyPropertyChangedFor(nameof(BadgeBorder))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(RequirementLines))]
    [NotifyPropertyChangedFor(nameof(Rank))]
    [NotifyPropertyChangedFor(nameof(Facts))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(HasTechnicalDetail))]
    [NotifyPropertyChangedFor(nameof(HasFix))]
    [NotifyPropertyChangedFor(nameof(HasCommand))]
    [NotifyPropertyChangedFor(nameof(SupportsKeyTest))]
    [NotifyPropertyChangedFor(nameof(SupportsRecordingTest))]
    [NotifyPropertyChangedFor(nameof(SupportsStoreTest))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandCommand))]
    public partial DiagnosticCheckResult Result { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Id => Result.Id;

    /// <summary>
    /// True for the injection checks: their category is the one the optional key test proves.
    /// The test is offered on the check it belongs to rather than on the page, so nobody starts
    /// it without having read what it does.
    /// </summary>
    public bool SupportsKeyTest => Category == DiagnosticCategory.InputInjection;

    /// <summary>True for the recording checks, which the interactive key-press test proves.</summary>
    public bool SupportsRecordingTest => Category == DiagnosticCategory.InputRecording;

    /// <summary>True for the plugin checks, where the online store test belongs.</summary>
    public bool SupportsStoreTest => Category == DiagnosticCategory.Plugins;


    public DiagnosticCategory Category => Result.Category;

    public string Title => Result.Title;

    /// <summary>The short form for the list column, falling back to the full sentence.</summary>
    public string ValueText => string.IsNullOrWhiteSpace(Result.Value) ? Result.Summary : Result.Value;

    /// <summary>The full sentence, shown in the detail pane.</summary>
    public string Description => Result.Summary;

    public string StatusText => DiagnosticText.StatusText(Result.Status);

    public IBrush StatusBrush => DiagnosticPalette.Dot(Result.Status);

    public IBrush BadgeFill => DiagnosticPalette.Fill(Result.Status);

    public IBrush BadgeBorder => DiagnosticPalette.Border(Result.Status);

    public IBrush BadgeText => DiagnosticPalette.Text(Result.Status);

    /// <summary>Sort weight: failures first, passes last.</summary>
    public int Rank => DiagnosticText.Rank(Result.Status);

    public bool HasTechnicalDetail => !string.IsNullOrWhiteSpace(Result.TechnicalDetail);

    public bool HasFix => Result.Fix != null;

    public bool HasCommand => !string.IsNullOrWhiteSpace(Result.Fix?.Command);

    /// <summary>
    /// The detail pane's key/value block: the result, its group, how long the check took, then
    /// whatever evidence the check collected.
    /// </summary>
    public IReadOnlyList<DiagnosticFact> Facts
    {
        get
        {
            List<DiagnosticFact> facts =
            [
                new(Loc.Tr("Diagnostics_FactResult"), ValueText),
                new(Loc.Tr("Diagnostics_FactGroup"), DiagnosticText.CategoryTitle(Result.Category)),
                new(Loc.Tr("Diagnostics_FactDuration"), $"{Result.Duration.TotalMilliseconds:F0} ms")
            ];

            facts.AddRange(Result.Evidence.Select(entry => new DiagnosticFact(entry.Key, entry.Value)));

            return facts;
        }
    }

    /// <summary>What the fix additionally requires, as ready-made sentences.</summary>
    public IReadOnlyList<string> RequirementLines
    {
        get
        {
            List<string> lines = [];

            if (Result.Fix == null)
            {
                return lines;
            }

            if (Result.Fix.RequiresElevation)
            {
                lines.Add(Loc.Tr("Diagnostics_RequiresElevation"));
            }

            if (Result.Fix.RequiresLogout)
            {
                lines.Add(Loc.Tr("Diagnostics_RequiresLogout"));
            }

            if (Result.Fix.RequiresReconnect)
            {
                lines.Add(Loc.Tr("Diagnostics_RequiresReconnect"));
            }

            return lines;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopyStatus))]
    public partial string CopyStatusText { get; set; } = string.Empty;

    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatusText);

    public IAsyncRelayCommand CopyCommandCommand => field ??= Relay.Create(CopyCommandAsync, () => HasCommand);

    private async Task CopyCommandAsync()
    {
        bool copied = await ClipboardHelper.SetTextAsync(Result.Fix.Command);
        CopyStatusText = copied ? Loc.Tr("Diagnostics_CommandCopied") : string.Empty;
    }
}
