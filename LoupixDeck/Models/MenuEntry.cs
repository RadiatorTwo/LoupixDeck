using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LoupixDeck.Models;

public class MenuEntry(string name, string command, string parentName = null, Dictionary<string, string> parameters = null)
    : INotifyPropertyChanged
{
    public string ParentName { get; set; } = parentName;
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;

    public Dictionary<string, string> Parameters { get; set; } = parameters ?? [];

    public ObservableCollection<MenuEntry> Children { get; set; } = [];

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
