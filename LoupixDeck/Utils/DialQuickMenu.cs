using Avalonia.Controls;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// The dial half of the device button context menu: one submenu per gesture over the command
/// catalogue, a preset submenu that fills all three at once, and a way into the full editor.
/// </summary>
/// <remarks>
/// Category submenus are created empty and filled the first time they are opened. The catalogue
/// holds every command a dial can run, across every loaded plugin, and building all of it — with
/// its glyphs — on each right-click is the cost a quick menu exists to avoid.
/// </remarks>
public static class DialQuickMenu
{
    /// <summary>Font the Material Design glyphs are drawn with, matching the rest of the app.</summary>
    private static readonly FontFamily SymbolFont = new(SymbolLibrary.FontUri);

    /// <summary>mdi-check — marks the action a gesture is bound to, and the category it sits in.</summary>
    private const string CheckGlyph = "\U000F012C";

    /// <summary>The items to put above a dial's clipboard entries, in order.</summary>
    public static IEnumerable<Control> BuildItems(RotaryButton dial, MainWindowViewModel vm)
    {
        DialQuickMenuViewModel menu = vm.DialMenu;

        foreach (RotaryAction gesture in RotaryButtonExtensions.Gestures)
            yield return BuildGestureMenu(dial, vm, menu, gesture);

        yield return BuildPresetMenu(dial, vm, menu);
        yield return new Separator();
        yield return new MenuItem
        {
            Header = "Advanced settings…",
            Command = vm.RotaryButtonCommand,
            CommandParameter = dial
        };
    }

    // ── One gesture ────────────────────────────────────────────────────────

    private static MenuItem BuildGestureMenu(RotaryButton dial, MainWindowViewModel vm,
        DialQuickMenuViewModel menu, RotaryAction gesture)
    {
        string current = dial.GetCommand(gesture);
        MenuEntry assigned = menu.FindAssigned(current);

        MenuItem item = new()
        {
            Header = gesture.DisplayName(),
            // The glyph of what is bound right now, so the three gestures can be read off the menu
            // without opening any of them.
            Icon = Glyph(assigned?.Icon)
        };

        item.Items.Add(new MenuItem
        {
            Header = "Remove",
            IsEnabled = !string.IsNullOrWhiteSpace(current),
            Command = Relay.Create(() => vm.ClearDialGestureAsync(dial, gesture))
        });

        item.Items.Add(new Separator());

        if (!menu.IsReady)
        {
            item.Items.Add(new MenuItem { Header = "Loading…", IsEnabled = false });
            return item;
        }

        foreach (MenuEntry group in menu.Catalogue)
        {
            // A preset group belongs to the preset submenu, not to a single gesture: binding it
            // here would drop two of its three commands.
            if (group.Name == Services.Commands.DialPresetMenuContributor.GroupName)
                continue;

            item.Items.Add(BuildCategory(dial, vm, group, gesture, assigned));
        }

        return item;
    }

    /// <summary>
    /// One catalogue category. Its leaves are built on first open; until then it holds a single
    /// placeholder, which is what keeps a submenu from being populated before anyone looks at it.
    /// </summary>
    private static MenuItem BuildCategory(RotaryButton dial, MainWindowViewModel vm, MenuEntry group,
        RotaryAction gesture, MenuEntry assigned)
    {
        // The tick on the category says "what this dial runs is in here", so it has to be resolved
        // before the category is ever opened.
        bool holdsAssigned = assigned != null && Contains(group, assigned);

        MenuItem item = new()
        {
            Header = group.Name,
            Icon = holdsAssigned ? Glyph(CheckGlyph) : Glyph(group.Icon)
        };

        item.Items.Add(new MenuItem { Header = "…", IsEnabled = false });

        bool filled = false;
        item.SubmenuOpened += (_, _) =>
        {
            if (filled)
                return;

            filled = true;
            item.Items.Clear();
            AddEntries(item, group, dial, vm, gesture, assigned);
        };

        return item;
    }

    private static void AddEntries(MenuItem parent, MenuEntry group, RotaryButton dial,
        MainWindowViewModel vm, RotaryAction gesture, MenuEntry assigned)
    {
        foreach (MenuEntry entry in group.Children)
        {
            bool isLeaf = !string.IsNullOrEmpty(entry.Command) || entry.IsCommandGroup;

            // A folder inside a category: a plugin's dynamic submenu, or a profile's workspaces.
            if (!isLeaf && entry.Children.Count > 0)
            {
                MenuItem folder = new()
                {
                    Header = entry.Name,
                    Icon = assigned != null && Contains(entry, assigned) ? Glyph(CheckGlyph) : Glyph(entry.Icon)
                };

                AddEntries(folder, entry, dial, vm, gesture, assigned);
                parent.Items.Add(folder);
                continue;
            }

            // A leaf with neither a command nor children is an info row the catalogue puts there to
            // explain itself ("Nothing to show"). It stays visible but does nothing.
            if (!isLeaf)
            {
                parent.Items.Add(new MenuItem { Header = entry.Name, IsEnabled = false });
                continue;
            }

            MenuEntry captured = entry;
            parent.Items.Add(new MenuItem
            {
                Header = entry.Name,
                Icon = ReferenceEquals(entry, assigned) ? Glyph(CheckGlyph) : Glyph(entry.Icon),
                Command = Relay.Create(() => vm.AssignDialGestureAsync(dial, gesture, captured))
            });
        }

        if (parent.Items.Count == 0)
            parent.Items.Add(new MenuItem { Header = "Nothing to show", IsEnabled = false });
    }

    private static bool Contains(MenuEntry group, MenuEntry entry)
    {
        foreach (MenuEntry child in group.Children)
        {
            if (ReferenceEquals(child, entry) || Contains(child, entry))
                return true;
        }

        return false;
    }

    // ── Presets ────────────────────────────────────────────────────────────

    private static MenuItem BuildPresetMenu(RotaryButton dial, MainWindowViewModel vm,
        DialQuickMenuViewModel menu)
    {
        MenuItem item = new() { Header = "Presets", Icon = Glyph(DialPreset.DefaultGlyph) };

        IReadOnlyList<DialPreset> presets = menu.Presets;
        DialPreset assigned = menu.FindAssignedPreset(dial);

        if (presets.Count == 0)
        {
            item.Items.Add(new MenuItem { Header = "No presets", IsEnabled = false });
            return item;
        }

        foreach (DialPreset preset in presets)
        {
            DialPreset captured = preset;
            item.Items.Add(new MenuItem
            {
                Header = preset.Name,
                Icon = ReferenceEquals(preset, assigned) ? Glyph(CheckGlyph) : Glyph(preset.Glyph),
                Command = Relay.Create(() => vm.ApplyDialPresetAsync(dial, captured))
            });
        }

        return item;
    }

    /// <summary>A Material Design glyph as a menu item icon, or null when there is none — Avalonia
    /// then simply leaves the icon column empty.</summary>
    private static Control Glyph(string glyph) => string.IsNullOrEmpty(glyph)
        ? null
        : new TextBlock { Text = glyph, FontFamily = SymbolFont };
}
