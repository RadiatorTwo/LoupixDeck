using System.Reflection;

namespace LoupixDeck.Services.Updates;

/// <summary>
/// The one place that knows the running app's version. Release builds get it from the tag
/// (<c>/p:Version=</c> in the release workflow) through the informational version attribute.
/// </summary>
public static class AppVersion
{
    /// <summary>Informational version without build metadata (the part after '+'), e.g. <c>1.26.0</c>.</summary>
    public static string Text { get; } = ReadText();

    /// <summary>The version as <c>major.minor.patch</c>, or null when it does not parse.</summary>
    public static Version Current { get; } = TryParse(Text);

    /// <summary>
    /// True for a local build without a real version: MSBuild's default is 1.0.0, a number no
    /// release carries. Such builds skip the automatic update check.
    /// </summary>
    public static bool IsDevelopmentBuild => Current is null || Current == new Version(1, 0, 0);

    /// <summary>
    /// Parses a stable SemVer core (<c>1.2.3</c>, optionally prefixed with 'v'). Pre-release or
    /// build suffixes (<c>1.2.3-beta</c>) are rejected, so only stable releases compare as versions.
    /// </summary>
    public static Version TryParse(string text)
    {
        string value = text?.Trim() ?? string.Empty;
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        string[] parts = value.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        int[] numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    private static string ReadText()
    {
        string full = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return full?.Split('+')[0];
    }
}
