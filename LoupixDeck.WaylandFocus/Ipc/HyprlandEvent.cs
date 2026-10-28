namespace LoupixDeck.WaylandFocus.Ipc;

/// <summary>One event line from Hyprland's event socket, formatted as "NAME>>DATA".</summary>
public readonly record struct HyprlandEvent(string Name, string Data)
{
    public static bool TryParse(string line, out HyprlandEvent parsed)
    {
        var separator = line.IndexOf(">>", StringComparison.Ordinal);
        if (separator <= 0)
        {
            parsed = default;
            return false;
        }

        parsed = new HyprlandEvent(line[..separator], line[(separator + 2)..]);
        return true;
    }
}