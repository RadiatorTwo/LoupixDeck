using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

public class RotaryButtonSettingsViewModel(RotaryButton buttonData) : ViewModelBase
{
    public RotaryButton ButtonData { get; set; } = buttonData;
}