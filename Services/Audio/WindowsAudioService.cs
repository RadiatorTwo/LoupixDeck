using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LoupixDeck.Services.Audio;

/// <summary>
/// Windows Core Audio implementation. Must only be instantiated when running on Windows;
/// non-Windows platforms get the <see cref="NoOpAudioService"/> from DI.
/// </summary>
public sealed class WindowsAudioService : IWindowsAudioService
{
    public bool IsSupported => true;

    public IReadOnlyList<AudioEndpointInfo> GetEndpoints(AudioEndpointKind kind)
    {
        using var enumerator = new MMDeviceEnumerator();
        var flow = kind == AudioEndpointKind.Render ? DataFlow.Render : DataFlow.Capture;

        string defaultId = null;
        try
        {
            using var def = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            defaultId = def.ID;
        }
        catch
        {
            // No default endpoint configured — leave defaultId null.
        }

        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        var result = new List<AudioEndpointInfo>(devices.Count);
        foreach (var d in devices)
        {
            try
            {
                result.Add(new AudioEndpointInfo(d.ID, d.FriendlyName, d.ID == defaultId));
            }
            finally
            {
                d.Dispose();
            }
        }
        return result;
    }

    public float GetVolume(string endpointId)
    {
        using var dev = GetDevice(endpointId);
        return dev?.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 0f;
    }

    public void SetVolume(string endpointId, float scalar01)
    {
        using var dev = GetDevice(endpointId);
        if (dev?.AudioEndpointVolume == null) return;
        dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar01, 0f, 1f);
    }

    public bool GetMute(string endpointId)
    {
        using var dev = GetDevice(endpointId);
        return dev?.AudioEndpointVolume?.Mute ?? false;
    }

    public void SetMute(string endpointId, bool muted)
    {
        using var dev = GetDevice(endpointId);
        if (dev?.AudioEndpointVolume == null) return;
        dev.AudioEndpointVolume.Mute = muted;
    }

    public IDisposable SubscribeVolumeChanges(string endpointId, Action<float, bool> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = enumerator.GetDevice(endpointId);
        }
        catch
        {
            enumerator.Dispose();
            return EmptyDisposable.Instance;
        }

        AudioEndpointVolumeNotificationDelegate handler = data =>
        {
            try { onChange(data.MasterVolume, data.Muted); }
            catch { /* swallow callback failure */ }
        };

        try
        {
            device.AudioEndpointVolume.OnVolumeNotification += handler;
        }
        catch
        {
            device.Dispose();
            enumerator.Dispose();
            return EmptyDisposable.Instance;
        }

        return new VolumeSubscription(device, enumerator, handler);
    }

    private static MMDevice GetDevice(string endpointId)
    {
        using var enumerator = new MMDeviceEnumerator();
        try { return enumerator.GetDevice(endpointId); }
        catch { return null; }
    }

    private sealed class VolumeSubscription : IDisposable
    {
        private MMDevice _device;
        private MMDeviceEnumerator _enumerator;
        private AudioEndpointVolumeNotificationDelegate _handler;

        public VolumeSubscription(MMDevice device, MMDeviceEnumerator enumerator,
            AudioEndpointVolumeNotificationDelegate handler)
        {
            _device = device;
            _enumerator = enumerator;
            _handler = handler;
        }

        public void Dispose()
        {
            try
            {
                if (_device?.AudioEndpointVolume != null && _handler != null)
                    _device.AudioEndpointVolume.OnVolumeNotification -= _handler;
            }
            catch { /* ignore */ }

            _device?.Dispose();
            _enumerator?.Dispose();
            _device = null;
            _enumerator = null;
            _handler = null;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
