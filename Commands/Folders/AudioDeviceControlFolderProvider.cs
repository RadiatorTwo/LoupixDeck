using Avalonia.Media;
using LoupixDeck.Services.Audio;
using LoupixDeck.Services.FolderNavigation;

namespace LoupixDeck.Commands.Folders;

/// <summary>
/// Sub-folder shown after the user picks a specific audio endpoint. Slot 0 displays the live
/// volume, slot 1 toggles mute. KNOB_TL adjusts the volume by 2% per tick.
/// </summary>
public sealed class AudioDeviceControlFolderProvider : FolderProviderBase
{
    private const float StepScalar = 0.02f;

    private readonly IWindowsAudioService _audio;
    private readonly AudioEndpointInfo _endpoint;

    private IDisposable _subscription;
    private float _currentVolume;
    private bool _currentMute;

    private readonly Dictionary<int, RotaryOverride> _rotaries;

    public AudioDeviceControlFolderProvider(IWindowsAudioService audio, AudioEndpointInfo endpoint)
    {
        _audio = audio;
        _endpoint = endpoint;

        _rotaries = new Dictionary<int, RotaryOverride>
        {
            [0] = new RotaryOverride
            {
                OnLeft  = () => { AdjustVolume(-StepScalar); return Task.CompletedTask; },
                OnRight = () => { AdjustVolume(+StepScalar); return Task.CompletedTask; },
                OnPress = () => { ToggleMute(); return Task.CompletedTask; }
            }
        };
    }

    public override string Title => _endpoint.FriendlyName;

    public override IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => _rotaries;

    public override void OnEnter()
    {
        _currentVolume = _audio.GetVolume(_endpoint.Id);
        _currentMute = _audio.GetMute(_endpoint.Id);

        _subscription = _audio.SubscribeVolumeChanges(_endpoint.Id, (vol, muted) =>
        {
            _currentVolume = vol;
            _currentMute = muted;
            RaiseEntriesChanged();
        });
    }

    public override void OnExit()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        var percent = (int)Math.Round(_currentVolume * 100f);

        return new[]
        {
            new FolderEntry
            {
                SlotIndex = 0,
                Text = $"{percent} %",
                BackColor = Color.FromRgb(0x20, 0x40, 0x60),
                TextSize = 22,
                Bold = true
            },
            new FolderEntry
            {
                SlotIndex = 1,
                Text = _currentMute ? "Unmute" : "Mute",
                BackColor = _currentMute
                    ? Color.FromRgb(0x70, 0x20, 0x20)
                    : Color.FromRgb(0x30, 0x30, 0x30),
                TextSize = 16,
                OnPress = () => { ToggleMute(); return Task.CompletedTask; }
            }
        };
    }

    private void AdjustVolume(float delta)
    {
        var next = Math.Clamp(_currentVolume + delta, 0f, 1f);
        _audio.SetVolume(_endpoint.Id, next);
        // Don't optimistically update — the volume notification callback will refresh the UI.
    }

    private void ToggleMute()
    {
        _audio.SetMute(_endpoint.Id, !_currentMute);
        // Notification callback will refresh.
    }
}
