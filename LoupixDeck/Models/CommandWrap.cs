using CommunityToolkit.Mvvm.ComponentModel;

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
[ObservableObject]
public partial class CommandWrap
{
    [ObservableProperty]
    public partial bool PreEnabled { get; set; }

    [ObservableProperty]
    public partial string PreCommands { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PostEnabled { get; set; }

    [ObservableProperty]
    public partial string PostCommands { get; set; } = string.Empty;

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
}
