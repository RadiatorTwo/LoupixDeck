namespace LoupixDeck.Services.Macros;

/// <summary>
/// Stand-in recorder for platforms without a capture backend (Linux today). Reports
/// itself as unsupported and never records, so the editor can disable the button and
/// show a hint instead of failing at runtime.
/// </summary>
public sealed class NoOpInputRecorder : IInputRecorder
{
    public bool IsSupported => false;
    public bool IsRecording => false;

    public event EventHandler<RecordedKeyEventArgs> KeyRecorded
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }
}
