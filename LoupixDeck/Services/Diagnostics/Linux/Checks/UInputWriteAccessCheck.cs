using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// The ground truth for macro playback and the virtual mouse: can this process open
/// /dev/uinput for writing?
///
/// The probe opens and closes the node immediately. Opening allocates a kernel uinput instance
/// but creates no input device - a device only appears on UI_DEV_CREATE - so this tests the
/// permission and injects nothing.
/// </summary>
public sealed class UInputWriteAccessCheck : ILinuxDiagnosticCheck, IExclusiveDiagnosticCheck
{
    public string Id => "uinput.write-access";

    public DiagnosticCategory Category => DiagnosticCategory.InputInjection;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        int errno = LinuxInputInterop.TryOpenAndClose(LinuxInputInterop.UinputPath,
            LinuxInputInterop.O_WRONLY | LinuxInputInterop.O_NONBLOCK);

        if (errno == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_UinputWriteOk")));
        }

        string detail = $"open(\"{LinuxInputInterop.UinputPath}\", O_WRONLY|O_NONBLOCK) = -1 (errno {errno})";

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["errno"] = errno.ToString()
        };

        if (errno == LinuxInputInterop.ENOENT)
        {
            // The node check already reported the real cause. Reporting it twice would read as
            // two separate problems.
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_UinputWriteNoNode"), detail));
        }

        if ((errno == LinuxInputInterop.EACCES) || (errno == LinuxInputInterop.EPERM))
        {
            DiagnosticFix fix = new(FixKind.Command, Loc.Tr("Diagnostics_FixJoinInputGroup"),
                "sudo usermod -aG input $USER", RequiresElevation: true, RequiresLogout: true);

            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_UinputWriteDeniedFmt", errno), detail, fix, evidence, $"errno {errno}"));
        }

        return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
            Loc.Tr("Diagnostics_UinputWriteUnknownFmt", errno), detail));
    }
}
