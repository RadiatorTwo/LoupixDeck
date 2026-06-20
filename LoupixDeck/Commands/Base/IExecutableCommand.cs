namespace LoupixDeck.Commands.Base
{
    public interface IExecutableCommand
    {
        Task Execute(string[] parameters);
    }
}
