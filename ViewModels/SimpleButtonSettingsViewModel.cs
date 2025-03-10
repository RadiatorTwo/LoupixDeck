using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel(SimpleButton buttonData) : ViewModelBase
{
    public SimpleButton ButtonData { get; set; } = buttonData;
}
