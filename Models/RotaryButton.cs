namespace LoupixDeck.Models;

public class RotaryButton : LoupedeckButton
{
    public RotaryButton()
    {
    }

    public RotaryButton(string rotaryLeftCommand, string rotaryRightCommand)
    {
        RotaryLeftCommand = rotaryLeftCommand;
        RotaryRightCommand = rotaryRightCommand;
    }
    
    public string RotaryLeftCommand { get; set; } = string.Empty;
    public string RotaryRightCommand { get; set; } = string.Empty;
}