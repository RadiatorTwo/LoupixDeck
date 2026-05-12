namespace LoupixDeck.Services.Audio;

/// <summary>
/// Fallback used on non-Windows so the audio commands remain instantiable. All operations
/// either return empty data or throw NotSupportedException for state-changing calls.
/// </summary>
public sealed class NoOpAudioService : IWindowsAudioService
{
    public bool IsSupported => false;

    public IReadOnlyList<AudioEndpointInfo> GetEndpoints(AudioEndpointKind kind) =>
        Array.Empty<AudioEndpointInfo>();

    public float GetVolume(string endpointId) => 0f;
    public void SetVolume(string endpointId, float scalar01) { }
    public bool GetMute(string endpointId) => false;
    public void SetMute(string endpointId, bool muted) { }

    public IDisposable SubscribeVolumeChanges(string endpointId, Action<float, bool> onChange) =>
        EmptyDisposable.Instance;

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
