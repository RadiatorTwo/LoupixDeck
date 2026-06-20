using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoupixDeck.Models;

/// <summary>
/// Extra commands that get chained around a button command at execution time:
/// PreCommands run first, then the button's own command, then PostCommands —
/// all joined with " &amp;&amp; " into a single ExecuteCommand call. Enabled
/// flags exist so the user can park a definition without losing the text.
///
/// Each page owns one or more of these slots (one per input type for rotary
/// pages, one shared for touch pages).
/// </summary>
public class CommandWrap : INotifyPropertyChanged
{
    private bool _preEnabled;
    public bool PreEnabled
    {
        get => _preEnabled;
        set { if (_preEnabled == value) return; _preEnabled = value; OnPropertyChanged(); }
    }

    private string _preCommands = string.Empty;
    public string PreCommands
    {
        get => _preCommands;
        set { if (_preCommands == value) return; _preCommands = value ?? string.Empty; OnPropertyChanged(); }
    }

    private bool _postEnabled;
    public bool PostEnabled
    {
        get => _postEnabled;
        set { if (_postEnabled == value) return; _postEnabled = value; OnPropertyChanged(); }
    }

    private string _postCommands = string.Empty;
    public string PostCommands
    {
        get => _postCommands;
        set { if (_postCommands == value) return; _postCommands = value ?? string.Empty; OnPropertyChanged(); }
    }

    /// <summary>Returns the chained command string for a given button command.</summary>
    public string Apply(string command)
    {
        if (string.IsNullOrEmpty(command)) return command;
        var pre = PreEnabled && !string.IsNullOrWhiteSpace(PreCommands) ? PreCommands.Trim() : null;
        var post = PostEnabled && !string.IsNullOrWhiteSpace(PostCommands) ? PostCommands.Trim() : null;
        if (pre == null && post == null) return command;

        var parts = new List<string>(3);
        if (pre != null) parts.Add(pre);
        parts.Add(command);
        if (post != null) parts.Add(post);
        return string.Join(" && ", parts);
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
