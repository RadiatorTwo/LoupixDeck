using System.Collections.Frozen;

namespace LoupixDeck.Models;

public class KeyboardLayout(string name, FrozenDictionary<char, (ushort keycode, bool shift)> keyMap)
{
    public string Name { get; } = name;
    public FrozenDictionary<char, (ushort keycode, bool shift)> KeyMap { get; } = keyMap;

    public KeyboardLayout(string name, IEnumerable<KeyValuePair<char, (ushort keycode, bool shift)>> keyMap) : this(name, keyMap.ToFrozenDictionary()) { }

    public bool TryGetKeycode(char c, out ushort keycode, out bool shift)
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