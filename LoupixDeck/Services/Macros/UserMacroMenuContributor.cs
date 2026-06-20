using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
// Both the app and the plugin SDK define IMenuContributor — this contributor implements the app-side one.
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Lists every user-defined macro as a leaf in a dedicated "User Macros" menu group.
/// Selecting an entry inserts <c>System.Macro(MacroName)</c> into the button command
/// (the CommandBuilder substitutes the entry's Name as the first parameter).
/// </summary>
public class UserMacroMenuContributor : IMenuContributor
{
    public const string GroupName = "User Macros";

    private readonly IMacroManager _macroManager;

    public UserMacroMenuContributor(IMacroManager macroManager)
    {
        _macroManager = macroManager;
    }

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        var macros = _macroManager.Macros;
        if (macros.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        var group = new MenuEntry(GroupName, string.Empty);
        foreach (var macro in macros.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            group.Children.Add(new MenuEntry(macro.Name, "System.Macro"));
        }

        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }
}
