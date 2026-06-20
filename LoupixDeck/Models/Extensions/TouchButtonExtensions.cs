using System.Collections.ObjectModel;

namespace LoupixDeck.Models.Extensions;

public static class TouchButtonExtensions
{
    public static TouchButton FindByIndex(this ObservableCollection<TouchButton> touchButtons, int index)
    {
        return touchButtons.FirstOrDefault(button => button.Index == index);
    }
}