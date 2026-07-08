using System.Collections.ObjectModel;
using System.ComponentModel;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Models;

public class MenuEntry(string name, string command, string parentName = null, Dictionary<string, string> parameters = null)
    : INotifyPropertyChanged
{
    public string ParentName { get; set; } = parentName;
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;

    public Dictionary<string, string> Parameters { get; set; } = parameters ?? [];

    public ObservableCollection<MenuEntry> Children { get; set; } = [];

    /// <summary>
    /// Optional MDI glyph for the command picker. On a group entry it is the category
    /// card icon; on a leaf it is the command-row icon (which falls back to the
    /// category icon when null). Purely cosmetic — never persisted.
    /// </summary>
    public string Icon { get; set; }

    /// <summary>Optional one-line description: the category card subtitle on a group
    /// entry, the command-row subtitle on a leaf. Purely cosmetic — never persisted.</summary>
    public string Description { get; set; }

    /// <summary>On a group entry, the picker section (Core / Macros / Plugins) the
    /// category is filed under. Null on leaves (and treated as Plugins when unset).</summary>
    public CommandGroupSection? Section { get; set; }

    /// <summary>
    /// When set, this entry is a rotary command group: each <see cref="RotaryAction"/>
    /// maps to a fully-built, ready-to-persist command string. Applying the group
    /// writes each action's string into the matching rotary slot. Null for normal
    /// command/folder entries.
    /// </summary>
    public IReadOnlyDictionary<RotaryAction, string> RotaryGroup { get; set; }

    /// <summary>True when this entry is a rotary command group (see
    /// <see cref="RotaryGroup"/>), used by the menu template to badge it.</summary>
    public bool IsCommandGroup => RotaryGroup is { Count: > 0 };

    private bool _isLoading;

    /// <summary>
    /// True while a slow plugin still assembles this group's dynamic submenus.
    /// The menu shows an inline "(loading…)" suffix after the group name and
    /// clears the flag once the plugin completes.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
                return;

            _isLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}