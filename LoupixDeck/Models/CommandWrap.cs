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
    public partial CommandBinding PreCommands { get; set; } = CommandBinding.Empty;

    [ObservableProperty]
    public partial bool PostEnabled { get; set; }

    [ObservableProperty]
    public partial CommandBinding PostCommands { get; set; } = CommandBinding.Empty;

    /// <summary>Returns the chain run for a given button command: pre commands, the command, post commands.</summary>
    public CommandBinding Apply(CommandBinding command)
    {
        if (CommandBinding.IsNullOrEmpty(command)) return command;
        CommandBinding pre = PreEnabled && !CommandBinding.IsNullOrEmpty(PreCommands) ? PreCommands : CommandBinding.Empty;
        CommandBinding post = PostEnabled && !CommandBinding.IsNullOrEmpty(PostCommands) ? PostCommands : CommandBinding.Empty;
        return pre.Append(command).Append(post);
    }
}
