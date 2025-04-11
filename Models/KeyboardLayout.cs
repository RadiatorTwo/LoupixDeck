namespace LoupixDeck.Models;

public class KeyboardLayout(string name, Dictionary<char, (int keycode, bool shift)> keyMap)
{
    public string Name { get; } = name;
    public Dictionary<char, (int keycode, bool shift)> KeyMap { get; } = keyMap;

    public bool TryGetKeycode(char c, out int keycode, out bool shift)
    {
        if (KeyMap.TryGetValue(c, out var entry))
        {
            keycode = entry.keycode;
            shift = entry.shift;
            return true;
        }

        keycode = 0;
        shift = false;
        return false;
    }
}