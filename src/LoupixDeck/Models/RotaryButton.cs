namespace LoupixDeck.Models;

public class RotaryButton(int index,string rotaryLeftCommand, string rotaryRightCommand) : LoupedeckButton
{
    public int Index { get; } = index;
    
    private string _rotaryLeftCommand = rotaryLeftCommand;
    private string _rotaryRightCommand = rotaryRightCommand;
    
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