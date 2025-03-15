using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Styling;

namespace LoupixDeck.Models
{
    public class TouchButtonPage : AvaloniaObject
    {
        public TouchButtonPage()
        {
            TouchButtons = [];
        }
        
        public TouchButtonPage(int pageSize)
        {
            TouchButtons = new TouchButton[pageSize];
        }

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<TouchButtonPage, bool>(nameof(IsSelected));
        
        public bool IsSelected
        {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }
        
        public int Page { get; set; }
        public TouchButton[] TouchButtons { get; set; }
    }
}
