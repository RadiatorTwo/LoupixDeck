using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Creates a virtual input device and destroys it again, which is the only way to prove that
/// macro playback would actually work rather than only that the node can be opened.
///
/// No input is ever sent: the device is created and destroyed back to back, and no input_event
/// is written. The name keeps the "Loupix" prefix on purpose - LinuxInputRecorder filters
/// devices with that prefix out, so a recording that is running right now never sees the probe.
/// </summary>
public sealed class UInputCreateProbeCheck : ILinuxDiagnosticCheck, IExclusiveDiagnosticCheck
{
    private const string ProbeDeviceName = "LoupixDeckDiagnosticProbe";

    public string Id => "uinput.create-probe";

    public DiagnosticCategory Category => DiagnosticCategory.InputInjection;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        int fileDescriptor = LinuxInputInterop.open(LinuxInputInterop.UinputPath,
            LinuxInputInterop.O_WRONLY | LinuxInputInterop.O_NONBLOCK);

        if (fileDescriptor < 0)
        {
            // The write-access check already reported why.
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_UinputProbeNoAccess")));
        }

        bool created = false;

        try
        {
            if (LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_SET_EVBIT,
                    LinuxInputInterop.EV_KEY) < 0)
            {
                return Task.FromResult(Failed(title, "UI_SET_EVBIT"));
            }

            // UI_DEV_CREATE rejects a device with no capability at all, so one unused key is set.
            if (LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_SET_KEYBIT,
                    LinuxInputInterop.KEY_F24) < 0)
            {
                return Task.FromResult(Failed(title, "UI_SET_KEYBIT"));
            }

            if (!LinuxInputInterop.WriteUserDev(fileDescriptor, ProbeDeviceName))
            {
                return Task.FromResult(Failed(title, "write(uinput_user_dev)"));
            }

            if (LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_DEV_CREATE, 0) < 0)
            {
                return Task.FromResult(Failed(title, "UI_DEV_CREATE"));
            }

            created = true;

            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_UinputProbeOk")));
        }
        finally
        {
            if (created)
            {
                LinuxInputInterop.ioctl(fileDescriptor, LinuxInputInterop.UI_DEV_DESTROY, 0);
            }

            LinuxInputInterop.close(fileDescriptor);
        }
    }

    private DiagnosticCheckResult Failed(string title, string step)
    {
        int errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();

        return DiagnosticCheckResult.Fail(Id, Category, title,
            Loc.Tr("Diagnostics_UinputProbeFailedFmt", errno), $"{step} failed with errno {errno}");
    }
}
