using System.Text;

namespace LoupixDeck.WaylandFocus;

/// <summary>
/// Scrubs text that originated from applications (window titles, app ids)
/// before it reaches a terminal or an output stream. Any application can
/// put arbitrary control characters in its window title; printing them
/// verbatim lets an app manipulate the operator's terminal (cursor moves,
/// title changes, OSC 52 clipboard writes in supporting terminals).
/// </summary>
public static class SafeText
{
    /// <summary>
    /// Removes every Unicode control character (C0, DEL, C1) from
    /// <paramref name="value"/>. Returns <see langword="null"/> for null.
    /// </summary>
    public static string? Strip(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsControl(character))
                builder.Append(character);
        }

        return builder.ToString();
    }
}