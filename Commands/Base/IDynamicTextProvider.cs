namespace LoupixDeck.Commands.Base;

public interface IDynamicTextProvider : IExecutableCommand
{
    TimeSpan UpdateInterval { get; }
    string GetText(string[] parameters);
}
