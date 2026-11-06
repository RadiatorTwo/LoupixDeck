using System.Diagnostics;
using System.Runtime.Versioning;
using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// macOS implementation. Activation is LaunchServices' job: <c>open</c> on the bundle of a
/// running application brings it forward instead of starting a second instance, and does
/// nothing visible when it already is frontmost. This only has to find the bundle the running
/// process belongs to.
/// </summary>
/// <remarks>
/// A <c>.app</c> target needs none of this — <c>LaunchAppCommand</c> starts it through
/// <c>open</c>, which already behaves as single instance. This covers the targets that are not
/// bundles, such as a <c>steam://</c> URI with a process name filled in.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacAppWindowActivator : IAppWindowActivator
{
    private const string BundleMarker = ".app/";

    public Task<AppActivationOutcome> ActivateAsync(string processName, CancellationToken cancellationToken)
        => Task.Run(() => Activate(processName), cancellationToken);

    private static AppActivationOutcome Activate(string processName)
    {
        Process[] processes = Process.GetProcessesByName(ContextRuleMatcher.Normalize(processName));
        try
        {
            if (processes.Length == 0)
                return AppActivationOutcome.NotRunning;

            string bundle = processes.Select(BundleOf).FirstOrDefault(b => b != null);
            if (bundle == null)
            {
                // A process outside any bundle has no Dock presence LaunchServices could raise.
                return AppActivationOutcome.Unsupported;
            }

            ProcessStartInfo start = new("open") { UseShellExecute = false };
            start.ArgumentList.Add(bundle);
            using Process open = Process.Start(start);
            return AppActivationOutcome.Activated;
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    /// <summary>The outermost <c>.app</c> directory around the process's executable, or null.</summary>
    private static string BundleOf(Process process)
    {
        try
        {
            string path = process.MainModule?.FileName;
            int index = path?.IndexOf(BundleMarker, StringComparison.OrdinalIgnoreCase) ?? -1;
            return index < 0 ? null : path[..(index + BundleMarker.Length - 1)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
            return null; // exited, or not ours to inspect
        }
    }
}
