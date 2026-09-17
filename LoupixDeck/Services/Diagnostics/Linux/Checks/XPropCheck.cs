using System.Diagnostics;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Page switching by application is driven by an <c>xprop -spy</c> subprocess, so the whole
/// feature rests on one binary being present and runnable. Without an X server there is nothing
/// for it to talk to, which the XWayland check already reports - this one is only about xprop
/// itself.
/// </summary>
public sealed class XPropCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.xprop";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public async Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        if (string.IsNullOrWhiteSpace(LinuxSystemFacts.Display()))
        {
            return DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_XPropNoDisplay"), null, Loc.Tr("Diagnostics_ValueNoDisplay"));
        }

        string path = Which("xprop");

        if (path == null)
        {
            DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixInstallXProp"));

            return DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_XPropMissing"), null, fix, null, Loc.Tr("Diagnostics_ValueMissing"));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["xprop"] = path
        };

        (int exitCode, string output) = await RunAsync(path, "-root _NET_ACTIVE_WINDOW", cancellationToken);

        if (exitCode != 0)
        {
            return DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_XPropNoActiveWindowProperty"), output, null, evidence,
                Loc.Tr("Diagnostics_ValueNoProperty"));
        }

        evidence["active_window_property"] = output.Trim();

        return DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_XPropOk"), null, evidence, Loc.Tr("Diagnostics_ValueAvailable"));
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception)
        {
            // It exited on its own between the check and the kill. Nothing to do.
        }
    }

    /// <summary>The absolute path of a binary on PATH, or null when it is not there.</summary>
    private static string Which(string binary)
    {
        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string directory in pathVariable.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, binary);

            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // An unreadable PATH entry says nothing about the binary. Keep looking.
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo);

        if (process == null)
        {
            return (-1, string.Empty);
        }

        try
        {
            // Both pipes are drained at once: a child that fills the stderr buffer while nobody
            // reads it blocks on write and never exits.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(output, error);
            await process.WaitForExitAsync(cancellationToken);

            return (process.ExitCode, string.IsNullOrWhiteSpace(output.Result)
                ? error.Result
                : output.Result);
        }
        catch (OperationCanceledException)
        {
            // The check timed out or the run was cancelled. Leaving the child behind would
            // leak an xprop per run.
            Kill(process);
            throw;
        }
    }
}
