using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>Checks that /dev/uinput exists at all, i.e. that the kernel module is loaded.</summary>
public sealed class UInputNodeCheck : ILinuxDiagnosticCheck
{
    public string Id => "uinput.node";

    public DiagnosticCategory Category => DiagnosticCategory.InputInjection;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        if (File.Exists(LinuxInputInterop.UinputPath))
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_UinputNodePresent")));
        }

        DiagnosticFix fix = new(FixKind.Command, Loc.Tr("Diagnostics_FixLoadUinput"),
            "sudo modprobe uinput", RequiresElevation: true);

        return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
            Loc.Tr("Diagnostics_UinputNodeMissing"), null, fix));
    }
}
