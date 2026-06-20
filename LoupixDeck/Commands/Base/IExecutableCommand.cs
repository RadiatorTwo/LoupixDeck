using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoupixDeck.Commands.Base
{
    public interface IExecutableCommand
    {
        Task Execute(string[] parameters);
    }
}
