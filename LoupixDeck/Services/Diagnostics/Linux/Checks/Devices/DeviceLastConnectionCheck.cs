using LoupixDeck.Localization;
using LoupixDeck.LoupedeckDevice.Serial;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Reports how the app's own last attempt to open this deck ended. That is the one thing a
/// permission probe cannot tell: the port may be readable now and still have been taken at the
/// moment the app started, and a handshake failure looks like nothing at all from outside.
///
/// The attempt is read from <see cref="SerialConnectionDiagnostics"/>, which the serial layer
/// fills in as it opens ports - the diagnostics never open anything themselves.
/// </summary>
internal sealed class DeviceLastConnectionCheck(LinuxDeckDevice device) : ILinuxDiagnosticCheck
{
    public string Id => $"device.last-connection:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DeviceCheckTitle.For(Id, device);

        SerialAttempt attempt = device.PortNames
            .Select(SerialConnectionDiagnostics.For)
            .Where(entry => entry != null)
            .OrderByDescending(entry => entry.At)
            .FirstOrDefault();

        if (attempt == null)
        {
            // Nothing was ever opened on this port, so there is no attempt to report on. The
            // link check states whether the deck is in use.
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceNoAttempt"), null, Loc.Tr("Diagnostics_ValueNoAttempt")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["port"] = attempt.Port,
            ["baud_rate"] = attempt.BaudRate.ToString(),
            ["at"] = attempt.At.ToString("yyyy-MM-dd HH:mm:ss")
        };

        return Task.FromResult(attempt.Outcome switch
        {
            SerialAttemptOutcome.Opened => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAttemptOpenedFmt", attempt.Port), null, evidence,
                Loc.Tr("Diagnostics_ValueOpened")),

            SerialAttemptOutcome.Denied => DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAttemptDeniedFmt", attempt.Port), attempt.Detail,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixInstallDeviceRule"),
                    "./install-loupixdeck.sh", RequiresElevation: true, RequiresReconnect: true),
                evidence, Loc.Tr("Diagnostics_ValueDenied")),

            SerialAttemptOutcome.Missing => DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAttemptMissingFmt", attempt.Port), attempt.Detail,
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixReplugDevice"), null,
                    RequiresReconnect: true),
                evidence, Loc.Tr("Diagnostics_ValueMissing")),

            SerialAttemptOutcome.HandshakeFailed => DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAttemptHandshakeFmt", attempt.Port), attempt.Detail,
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixReplugDevice"), null,
                    RequiresReconnect: true),
                evidence, Loc.Tr("Diagnostics_ValueHandshake")),

            _ => DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAttemptOtherFmt", attempt.Port), attempt.Detail,
                Loc.Tr("Diagnostics_ValueFailed"))
        });
    }
}
