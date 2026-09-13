using LoupixDeck.Commands;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Actions;

/// <summary>Localization keys of the question asked before a prompting command is assigned.</summary>
public sealed record PanelParameterPrompt(string TitleKey, string PlaceholderKey, string InvalidTitleKey);

/// <summary>
/// The panel commands whose only parameter has to come from the user, and how a typed value becomes
/// the stored command and the key's caption. Host-side on purpose: plugin commands are unaffected.
/// </summary>
public static class PanelParameterPrompts
{
    private const int FallbackLabelLength = 24;

    private static readonly PanelParameterPrompt ShellPrompt =
        new("Prompt_ShellCommandTitle", "Prompt_ShellCommandPlaceholder", "Prompt_ShellCommandTitle");

    private static readonly PanelParameterPrompt UrlPrompt =
        new("Prompt_OpenUrlTitle", "Prompt_OpenUrlPlaceholder", "Prompt_OpenUrlInvalidTitle");

    /// <summary>The question for <paramref name="commandName"/>, or null when it does not prompt.</summary>
    public static PanelParameterPrompt For(string commandName) => commandName switch
    {
        ShellCommand.CommandName => ShellPrompt,
        OpenUrlCommand.CommandName => UrlPrompt,
        _ => null
    };

    /// <summary>
    /// Builds the stored command and the caption. False when the value is not usable (blank, or an
    /// address that is not http/https); nothing should be written then.
    /// </summary>
    public static bool TryBuild(string commandName, string value, out string command, out string label)
    {
        command = null;
        label = null;
        string text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return false;

        switch (commandName)
        {
            case ShellCommand.CommandName:
                // Stored verbatim, exactly as the button editor's free-text shell card stores it; the
                // unknown-command path of the command service runs it through the shell.
                command = text;
                label = ShellLabel(text);
                return true;

            case OpenUrlCommand.CommandName:
                if (!OpenUrlCommand.TryNormalize(text, out Uri uri))
                    return false;

                command = $"{OpenUrlCommand.CommandName}({CommandParameterEncoding.Encode(uri.AbsoluteUri)})";
                label = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// File name of the program a command line starts, e.g. "notepad" for
    /// <c>"C:\Windows\notepad.exe" file.txt</c>. Both separators are handled on every platform, so a
    /// Windows path copied into a config reads the same on Linux.
    /// </summary>
    private static string ShellLabel(string text)
    {
        string first = text.StartsWith('"')
            ? text[1..].Split('"')[0]
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        string fileName = first[(first.LastIndexOfAny(['/', '\\']) + 1)..];
        int dot = fileName.LastIndexOf('.');
        string name = dot > 0 ? fileName[..dot] : fileName;

        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return text.Length <= FallbackLabelLength ? text : text[..FallbackLabelLength];
    }
}
