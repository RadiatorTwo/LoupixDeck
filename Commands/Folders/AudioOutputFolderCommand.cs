using LoupixDeck.Commands.Base;
using LoupixDeck.Services.Audio;
using LoupixDeck.Services.FolderNavigation;

namespace LoupixDeck.Commands.Folders;

[Command("Audio.OutputDevices", "Audio: Output Devices", "Audio", Platform = CommandPlatform.Windows)]
public sealed class AudioOutputFolderCommand(
    IFolderNavigationService nav,
    IWindowsAudioService audio) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        nav.OpenFolder(new AudioDevicesFolderProvider(audio, AudioEndpointKind.Render));
}
