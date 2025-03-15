using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoupixDeck.Models
{
    public class TouchButtonPage
    {
        public TouchButtonPage()
        {
            TouchButtons = [];
        }
        
        public TouchButtonPage(int pageSize)
        {
            TouchButtons = new TouchButton[pageSize];
        }

        public int Page { get; set; }
        public TouchButton[] TouchButtons { get; set; }
    }
}
