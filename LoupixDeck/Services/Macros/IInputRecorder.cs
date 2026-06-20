namespace LoupixDeck.Services.Macros;

/// <summary>A single recorded key transition with the gap since the previous event.</summary>
public sealed class RecordedKeyEventArgs(string keyName, bool isDown, TimeSpan sinceLast) : EventArgs
{
    /// <summary>Canonical key name (the same names <see cref="Utils.KeyNames"/> understands).</summary>
    public string KeyName { get; } = keyName;

    /// <summary>True for a press, false for a release.</summary>
    public bool IsDown { get; } = isDown;

    /// <summary>Time elapsed since the previously recorded event (zero for the first one).</summary>
    public TimeSpan SinceLast { get; } = sinceLast;
}

/// <summary>
/// Captures real keyboard input globally so the macro editor can record a sequence of
/// Key Down / Key Up steps. Implementations are OS-specific; unsupported platforms
/// expose <see cref="IsSupported"/> = false and never raise events.
/// </summary>
public interface IInputRecorder
{
    /// <summary>False on platforms without a recording backend (e.g. Linux for now).</summary>
    bool IsSupported { get; }

    bool IsRecording { get; }

    /// <summary>
    /// Raised for every captured key transition. May fire on a background thread —
    /// subscribers that touch UI state must marshal to the UI thread themselves.
    /// </summary>
    event EventHandler<RecordedKeyEventArgs> KeyRecorded;

    void Start();
    void Stop();
}
