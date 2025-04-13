using Avalonia;

namespace LoupixDeck.Models;

public class RotaryButtonPage : AvaloniaObject
{
    public RotaryButtonPage(int pageSize)
    {
        RotaryButtons = new RotaryButton[pageSize];

        for (var i = 0; i < RotaryButtons.Length; i++)
        {
            var newButton = new RotaryButton(i, string.Empty, string.Empty);
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
    
    private static readonly DirectProperty<TouchButtonPage, int> PageDirectProperty =
        AvaloniaProperty.RegisterDirect<TouchButtonPage, int>(
            nameof(Page),
            o => o.Page,
            (o, v) => o.Page = v);
        
    private int _page;

    public int Page
    {
        get => _page;
        set => SetAndRaise(PageDirectProperty, ref _page, value);
    }
    
    public RotaryButton[] RotaryButtons { get; set; }
}