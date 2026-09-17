using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Shows the diagnostic report before anything leaves the machine. The user reads the exact text
/// that will be copied or saved - the report is already sanitized, but seeing it is the point.
/// </summary>
public sealed partial class DiagnosticReportViewModel : DialogViewModelBase<DialogResult>
{
    /// <summary>The report text, exactly as it will be copied or saved.</summary>
    public string ReportText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>True while a result of the last copy or save is worth showing.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>Raised when the dialog should close.</summary>
    public event Action CloseWindow;

    /// <summary>Supplies the report text. Call before showing the dialog.</summary>
    public void Initialize(string reportText) => ReportText = reportText ?? string.Empty;

    public IAsyncRelayCommand CopyCommand => field ??= Relay.Create(CopyAsync);

    public IAsyncRelayCommand SaveCommand => field ??= Relay.Create(SaveAsync);

    public IRelayCommand CloseCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    });

    private async Task CopyAsync()
    {
        bool copied = await ClipboardHelper.SetTextAsync(ReportText);

        // The dialog deliberately stays open: on some Wayland compositors the clipboard is
        // dropped when the window that owns it disappears.
        StatusText = Loc.Tr(copied ? "Diagnostics_ReportCopied" : "Diagnostics_ReportCopyFailed");
    }

    private async Task SaveAsync()
    {
        string suggested = $"loupixdeck-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.md";
        string path = await FileDialogHelper.SaveMarkdownDialog(null, suggested);

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, ReportText);
            StatusText = Loc.Tr("Diagnostics_ReportSavedFmt", path);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Tr("Diagnostics_ReportSaveFailedFmt", ex.Message);
        }
    }
}
