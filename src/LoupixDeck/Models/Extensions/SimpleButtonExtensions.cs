using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models.Extensions;

public static class SimpleButtonExtensions
{
    public static SimpleButton FindById(this SimpleButton[] buttons, Constants.ButtonType id)
    {
        return buttons?.FirstOrDefault(button => button.Id == id);
    }
}