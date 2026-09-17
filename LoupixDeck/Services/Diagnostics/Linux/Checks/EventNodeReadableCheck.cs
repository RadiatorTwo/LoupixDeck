using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// The ground truth for macro recording: can any keyboard event device be opened for reading?
///
/// Working playback says nothing about this. Playback writes to /dev/uinput, recording reads
/// /dev/input/event*, and the two are governed by different permissions.
/// </summary>
public sealed class EventNodeReadableCheck : ILinuxDiagnosticCheck, IExclusiveDiagnosticCheck
{
    public string Id => "recording.readable-device";

    public DiagnosticCategory Category => DiagnosticCategory.InputRecording;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        IReadOnlyList<EventDeviceCandidate> candidates = LinuxEventDeviceFacts.Discover();

        if (candidates == null)
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_EventListUnreadable")));
        }

        int keyboardCount = candidates.Count(candidate => candidate.IsKeyboard);

        if (keyboardCount == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_EventNoKeyboards")));
        }

        (int keyboards, int pointers, int firstErrno) =
            LinuxEventDeviceFacts.ProbeReadable(candidates, cancellationToken);

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["readable_keyboards"] = $"{keyboards}/{keyboardCount}",
            ["readable_pointers"] = pointers.ToString()
        };

        if (keyboards == 0)
        {
            DiagnosticFix fix = new(FixKind.Command, Loc.Tr("Diagnostics_FixJoinInputGroup"),
                "sudo usermod -aG input $USER", RequiresElevation: true, RequiresLogout: true);

            // Same wording LinuxInputRecorder writes to stderr today, so a user who saw that
            // message recognizes it here.
            string detail = "No readable keyboard device found under /dev/input. Recording needs " +
                            $"read access (errno {firstErrno}).";

            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_EventReadableDenied"), detail, fix, evidence));
        }

        if ((pointers == 0) && candidates.Any(candidate => candidate.IsPointer))
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_EventPointerNotReadableFmt", keyboards, keyboardCount),
                null, null, evidence));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_EventReadableOkFmt", keyboards, keyboardCount), null, evidence,
            $"{keyboards}/{keyboardCount}"));
    }
}
