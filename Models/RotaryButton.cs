namespace LoupixDeck.Models;

public class RotaryButton : LoupedeckButton
{
    private string _rotaryLeftCommand = string.Empty;
    private string _rotaryRightCommand = string.Empty;

    public RotaryButton()
    {
    }

    public RotaryButton(string rotaryLeftCommand, string rotaryRightCommand)
    {
        RotaryLeftCommand = rotaryLeftCommand;
        RotaryRightCommand = rotaryRightCommand;
    }

    public string RotaryLeftCommand
    {
        get => _rotaryLeftCommand;
        set
        {
            if (value == _rotaryLeftCommand) return;
            _rotaryLeftCommand = value;
            OnPropertyChanged(nameof(RotaryLeftCommand));
        }
    }

    public string RotaryRightCommand
    {
        get => _rotaryRightCommand;
        set
        {
            if (value == _rotaryRightCommand) return;
            _rotaryRightCommand = value;
            OnPropertyChanged(nameof(RotaryRightCommand));
        }
    }
}