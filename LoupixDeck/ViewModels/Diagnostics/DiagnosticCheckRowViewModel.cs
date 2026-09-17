using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>
/// One check in the diagnostics list. The status is flattened into booleans so the pill and the
/// detail blocks bind straight to IsVisible, which is how the plugin rows do it too.
/// </summary>
public sealed partial class DiagnosticCheckRowViewModel : ViewModelBase
{
    public DiagnosticCheckRowViewModel(DiagnosticCheckResult result)
    {
        Result = result;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPass))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsFail))]
    [NotifyPropertyChangedFor(nameof(IsNeutral))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(HasTechnicalDetail))]
    [NotifyPropertyChangedFor(nameof(HasEvidence))]
    [NotifyPropertyChangedFor(nameof(HasFix))]
    [NotifyPropertyChangedFor(nameof(HasCommand))]
    [NotifyPropertyChangedFor(nameof(EvidenceLines))]
    [NotifyPropertyChangedFor(nameof(RequirementLines))]
    public partial DiagnosticCheckResult Result { get; set; }

    public string Id => Result.Id;

    public bool IsPass => Result.Status == DiagnosticStatus.Pass;

    public bool IsWarning => Result.Status == DiagnosticStatus.Warning;

    public bool IsFail => Result.Status == DiagnosticStatus.Fail;

    /// <summary>Skipped and Unknown share the neutral pill - neither is a pass.</summary>
    public bool IsNeutral => Result.Status is DiagnosticStatus.Skipped or DiagnosticStatus.Unknown;

    public string StatusText => DiagnosticText.StatusText(Result.Status);

    public bool HasTechnicalDetail => !string.IsNullOrWhiteSpace(Result.TechnicalDetail);

    public bool HasEvidence => Result.Evidence.Count > 0;

    public bool HasFix => Result.Fix != null;

    public bool HasCommand => !string.IsNullOrWhiteSpace(Result.Fix?.Command);

    /// <summary>The evidence as "key: value" lines, for the collapsed detail block.</summary>
    public IReadOnlyList<string> EvidenceLines =>
        Result.Evidence.Select(entry => $"{entry.Key}: {entry.Value}").ToList();

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
