namespace LoupixDeck.Services.Macros;

/// <summary>
/// Watches for the user's global stop hotkey (configured in <see cref="IMacroManager.StopHotkey"/>)
/// and cancels all running macros when it is pressed. Reuses the platform input recorder as a
/// process-wide key listener, so it observes keys regardless of which window is focused.
/// </summary>
public interface IMacroStopHotkeyService : IDisposable
{
    /// <summary>Begins listening (idempotent). Reconfigures itself when the hotkey changes.</summary>
    void Start();
}

public sealed class MacroStopHotkeyService(IMacroManager macroManager, IMacroStopCoordinator coordinator)
    : IMacroStopHotkeyService
{
    private readonly object _lock = new();
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);

    // The configured combo (canonical key names); empty means the hotkey is disabled.
    private HashSet<string> _combo = new(StringComparer.OrdinalIgnoreCase);
    private bool _fired;
    private bool _started;

    // A dedicated global key listener (not the editor's recorder instance).
    private IInputRecorder _listener;

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        macroManager.MacrosChanged += (_, _) => Reconfigure();
        Reconfigure();
    }

    private void Reconfigure()
    {
        var combo = ParseCombo(macroManager.StopHotkey);

        lock (_lock)
        {
            _combo = combo;
            _pressed.Clear();
            _fired = false;
        }

        if (combo.Count == 0)
            StopListener();
        else
            StartListener();
    }

    private void StartListener()
    {
        if (_listener != null)
            return;

        var listener = CreateListener();
        if (listener is not { IsSupported: true })
            return;

        _listener = listener;
        _listener.KeyRecorded += OnKey;
        _listener.Start();
    }

    private void StopListener()
    {
        if (_listener == null)
            return;

        _listener.KeyRecorded -= OnKey;
        _listener.Stop();
        _listener = null;
    }

    private void OnKey(object sender, RecordedKeyEventArgs e)
    {
        var fire = false;

        var key = Utils.KeyNames.Canonicalize(e.KeyName);

        lock (_lock)
        {
            if (e.IsDown)
                _pressed.Add(key);
            else
                _pressed.Remove(key);

            // Fire once when the whole combo is held; re-arm only after it is released again.
            if (_combo.Count > 0 && _combo.IsSubsetOf(_pressed))
            {
                if (!_fired)
                {
                    _fired = true;
                    fire = true;
                }
            }
            else
            {
                _fired = false;
            }
        }

        if (fire)
            coordinator.CancelAll();
    }

    private static HashSet<string> ParseCombo(string hotkey)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(hotkey))
        {
            foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(Utils.KeyNames.Canonicalize(part));
        }

        return set;
    }

    private static IInputRecorder CreateListener()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsInputRecorder();
        if (OperatingSystem.IsLinux())
            return new LinuxInputRecorder();
        return null;
    }

    public void Dispose() => StopListener();
}
