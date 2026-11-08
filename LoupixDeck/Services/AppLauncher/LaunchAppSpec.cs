using LoupixDeck.Services.AppActivation;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// The decoded parameters of a <c>System.LaunchApp</c> command. Shared by the command itself and
/// the running indicator, so both resolve the same process name from the same string.
/// </summary>
/// <remarks>
/// Every parameter after the target is optional: a command saved with fewer values (or with an
/// empty one, which the editor writes for an unticked option it never saw) reads as "off".
/// </remarks>
public sealed record LaunchAppSpec(string Target, bool SingleInstance, string ProcessName, bool ShowRunningIndicator)
{
    public static LaunchAppSpec FromParameters(IReadOnlyList<string> parameters)
    {
        string target = parameters.Count > 0 ? CommandParameterEncoding.Decode(parameters[0]) : null;
        string processName = parameters.Count > 2 ? CommandParameterEncoding.Decode(parameters[2]) : null;
        return new LaunchAppSpec(target, Flag(parameters, 1), processName, Flag(parameters, 3));
    }

    /// <summary>
    /// Reads the <c>System.LaunchApp</c> segment of <paramref name="command"/>, or returns null
    /// when the command contains none. In a chain the first such segment counts.
    /// </summary>
    public static LaunchAppSpec FromCommand(string command)
    {
        foreach (string segment in CommandStringParser.SplitChain(command))
        {
            if (string.Equals(CommandStringParser.GetName(segment), LoupixDeck.Commands.LaunchAppCommand.CommandName, StringComparison.Ordinal))
                return FromParameters(CommandStringParser.GetParameters(segment));
        }

        return null;
    }

    /// <summary>The process this launch becomes; empty when it cannot be known.</summary>
    public string ResolveProcessName() => ResolveProcessName(Target, ProcessName);

    /// <summary>
    /// <paramref name="processName"/> when the user gave one, otherwise the name guessed from
    /// <paramref name="target"/> (see <see cref="LaunchTargetProcess.Guess"/>).
    /// </summary>
    public static string ResolveProcessName(string target, string processName)
        => string.IsNullOrWhiteSpace(processName)
            ? LaunchTargetProcess.Guess(target)
            : processName.Trim();

    private static bool Flag(IReadOnlyList<string> parameters, int index)
        => parameters.Count > index && bool.TryParse(parameters[index], out bool value) && value;
}
