using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports whether the PipeWire socket is there. Presence only - connecting would be a side
/// effect, and the socket is what an audio plugin needs in the first place.
/// </summary>
public sealed class PipeWireSocketCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.pipewire";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string runtimeDirectory = LinuxSystemFacts.RuntimeDirectory();

        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_RuntimeDirUnknown")));
        }

        string socket = Path.Combine(runtimeDirectory, "pipewire-0");

        // File.Exists returns true for a unix socket, so this needs no connect.
        if (File.Exists(socket))
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_PipeWireRunning"), socket));
        }

        if (!Directory.Exists(runtimeDirectory))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_RuntimeDirUnknown"), runtimeDirectory));
        }

        return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
            Loc.Tr("Diagnostics_PipeWireMissing"), socket));
    }
}
