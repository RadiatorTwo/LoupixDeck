#if WINDOWS
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LoupixDeck.Services.SystemPower;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemPowerService : ISystemPowerService, IDisposable
{
    public event EventHandler Suspending;
    public event EventHandler Resuming;

    private bool _started;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Suspending?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                Resuming?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        if (!_started) return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
#endif
