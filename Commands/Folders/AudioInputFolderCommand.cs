using LoupixDeck.Commands.Base;
using LoupixDeck.Services.Audio;
using LoupixDeck.Services.FolderNavigation;

namespace LoupixDeck.Commands.Folders;

[Command("Audio.InputDevices", "Audio: Input Devices", "Audio")]
public sealed class AudioInputFolderCommand(
    IFolderNavigationService nav,
    IWindowsAudioService audio) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        nav.OpenFolder(new AudioDevicesFolderProvider(audio, AudioEndpointKind.Capture));
}
