using Avalonia;

namespace LoupixDeck.Models;

public class RotaryButtonPage : AvaloniaObject
{
    public RotaryButtonPage()
    {
        RotaryButtons = [];
    }

    public RotaryButtonPage(int pageSize)
    {
        RotaryButtons = new RotaryButton[pageSize];

        for (int i = 0; i < RotaryButtons.Length; i++)
        {
            var newButton = new RotaryButton(string.Empty, string.Empty);
            RotaryButtons[i] = newButton;
        }
    }

    private static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<TouchButtonPage, bool>(nameof(IsSelected));
    
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
    
    public int Page { get; set; }
    public RotaryButton[] RotaryButtons { get; set; }
}