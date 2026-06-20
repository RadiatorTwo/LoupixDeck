using LoupixDeck.Commands.Base;

namespace LoupixDeck.Commands.DynamicText;

[Command("DynamicText.Clock", "Clock", "Dynamic Text",
    parameterTemplate: "({Format})",
    parameterNames: ["Format"],
    parameterTypes: [typeof(string)])]
public class ClockCommand : IDynamicTextProvider
{
    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    private const string DefaultFormat = "HH:mm:ss";
    private static readonly HashSet<char> DateTimeFormatChars =
        ['y', 'M', 'd', 'H', 'h', 'm', 's', 'f', 'F', 't', 'z', 'K', 'g'];

    public string GetText(string[] parameters)
    {
        var format = (parameters is { Length: > 0 } && !string.IsNullOrWhiteSpace(parameters[0])
                      && ContainsDateTimeSpecifier(parameters[0]))
            ? parameters[0]
            : DefaultFormat;

        try
        {
            return DateTime.Now.ToString(format);
        }
        catch (FormatException)
        {
            return DateTime.Now.ToString(DefaultFormat);
        }
    }

    private static bool ContainsDateTimeSpecifier(string format)
    {
        foreach (var c in format)
        {
            if (DateTimeFormatChars.Contains(c))
                return true;
        }
        return false;
    }

    public Task Execute(string[] parameters) => Task.CompletedTask;
}
