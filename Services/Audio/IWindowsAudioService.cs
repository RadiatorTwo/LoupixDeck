namespace LoupixDeck.Services.Audio;

public enum AudioEndpointKind { Render, Capture }

public sealed record AudioEndpointInfo(string Id, string FriendlyName, bool IsDefault);

public interface IWindowsAudioService
{
    bool IsSupported { get; }

    IReadOnlyList<AudioEndpointInfo> GetEndpoints(AudioEndpointKind kind);

    /// <summary>0..1 scalar.</summary>
    float GetVolume(string endpointId);

    /// <summary>0..1 scalar — clamped internally.</summary>
    void SetVolume(string endpointId, float scalar01);

    bool GetMute(string endpointId);
    void SetMute(string endpointId, bool muted);

    /// <summary>
    /// Subscribes to volume/mute notifications for an endpoint. Dispose the returned token
    /// to unsubscribe.
    /// </summary>
    IDisposable SubscribeVolumeChanges(string endpointId, Action<float, bool> onChange);
}
