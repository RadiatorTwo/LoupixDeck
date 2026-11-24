using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Localization;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Utils;

/// <summary>
/// Selection and right-click menu of the command sequence strips in the button editors (issue #684).
/// A click on a chip selects it, Ctrl adds or removes one, Shift selects a range. The menu copies,
/// cuts, duplicates or removes the selected commands, or pastes after them; on the strip itself it
/// pastes at the end. Either way it can copy the whole sequence. A copied command keeps the values
/// the user set, so it lands on another button, gesture or device exactly as configured.
///
/// Attached once per editor window: the events bubble up from the chip, so no per-chip wiring is
/// needed. A strip is any <c>Border.sequence-strip</c>; its chain is the view model behind the chip
/// list inside it. Only one strip of a window holds a selection at a time.
/// </summary>
public static class CommandChainMenu
{
    public static void Attach(Window window)
    {
        window.AddHandler(InputElement.ContextRequestedEvent, OnContextRequested);
        // Tunnel and handled-too: the editors set the active strip on the same press and the chip
        // buttons handle theirs, and the selection must not depend on who saw the press first.
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window || e.Source is not Visual source) return;
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;

        if (!TryResolve(source, out Border strip, out ICommandChain chain)) return;

        // The grip starts a reorder and the chip buttons open or remove; neither changes the selection.
        if (source.GetSelfAndVisualAncestors().TakeWhile(v => v != strip)
            .Any(v => v is Button || (v is Border b && b.Classes.Contains("drag-handle"))))
            return;

        CommandSegment segment = FindSegment(source, strip);
        ClearOtherChains(window, chain);

        if (segment == null)
        {
            SelectOnly(chain, null);
            return;
        }

        KeyModifiers toggle = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && chain.Commands.FirstOrDefault(s => s.IsSelected) is { } anchor)
        {
            int from = chain.Commands.IndexOf(anchor);
            int to = chain.Commands.IndexOf(segment);
            if (from > to) (from, to) = (to, from);
            for (int i = 0; i < chain.Commands.Count; i++)
                chain.Commands[i].IsSelected = i >= from && i <= to;
        }
        else if (e.KeyModifiers.HasFlag(toggle))
        {
            segment.IsSelected = !segment.IsSelected;
        }
        else
        {
            SelectOnly(chain, segment);
        }
    }

    private static void OnContextRequested(object sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || e.Source is not Visual source) return;
        if (!TryResolve(source, out Border strip, out ICommandChain chain)) return;

        ICommandClipboardService clipboard = Program.AppServices?.GetService<ICommandClipboardService>();
        if (clipboard == null) return;

        CommandSegment segment = FindSegment(source, strip);
        if (sender is Window window)
            ClearOtherChains(window, chain);

        MenuFlyout menu = new();

        if (segment != null)
        {
            // Right-clicking outside the selection makes that chip the selection, as in a file list.
            if (!segment.IsSelected)
                SelectOnly(chain, segment);

            List<CommandSegment> selected = chain.Commands.Where(s => s.IsSelected).ToList();
            bool hasRaw = selected.Any(s => !string.IsNullOrWhiteSpace(s.Raw));

            Add(menu, "ButtonMenu_Copy", () => clipboard.Copy(selected.Select(s => s.Raw)), hasRaw);
            Add(menu, "ButtonMenu_Cut", () =>
            {
                clipboard.Copy(selected.Select(s => s.Raw));
                Remove(chain, selected);
            }, hasRaw);
            Add(menu, "CommandMenu_PasteAfter",
                () => Insert(chain, clipboard.Commands, AfterLast(chain, selected)), clipboard.HasData);
            Add(menu, "CommandMenu_Duplicate",
                () => Insert(chain, selected.Select(s => s.Raw).ToList(), AfterLast(chain, selected)), hasRaw);
            Add(menu, "CommandMenu_Remove", () => Remove(chain, selected));

            // When everything is selected, Copy already is the whole sequence.
            if (selected.Count < chain.Commands.Count)
            {
                menu.Items.Add(new Separator());
                Add(menu, "CommandMenu_CopyAll", () => CopyAll(chain, clipboard));
            }
        }
        else
        {
            SelectOnly(chain, null);
            if (chain.Commands.Count > 0)
                Add(menu, "CommandMenu_CopyAll", () => CopyAll(chain, clipboard));
            Add(menu, "ButtonMenu_Paste",
                () => Insert(chain, clipboard.Commands, chain.Commands.Count), clipboard.HasData);
        }

        Control anchor = segment == null
            ? strip
            : source.GetSelfAndVisualAncestors().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("command-chip")) ?? (Control)strip;
        menu.ShowAt(anchor, showAtPointer: true);
        e.Handled = true;
    }

    /// <summary>The strip around <paramref name="source"/> and the chain behind it.</summary>
    private static bool TryResolve(Visual source, out Border strip, out ICommandChain chain)
    {
        strip = source.GetSelfAndVisualAncestors().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("sequence-strip"));
        chain = strip == null ? null : ChainOf(strip);
        return chain != null;
    }

    private static ICommandChain ChainOf(Visual strip) =>
        strip.GetVisualDescendants().OfType<ItemsControl>()
            .Select(list => list.DataContext).OfType<ICommandChain>().FirstOrDefault();

    private static CommandSegment FindSegment(Visual source, Visual strip) =>
        source.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != strip)
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<CommandSegment>()
            .FirstOrDefault();

    private static void SelectOnly(ICommandChain chain, CommandSegment segment)
    {
        foreach (CommandSegment s in chain.Commands)
            s.IsSelected = s == segment;
    }

    private static void ClearOtherChains(Window window, ICommandChain keep)
    {
        foreach (Border strip in window.GetVisualDescendants().OfType<Border>()
                     .Where(b => b.Classes.Contains("sequence-strip")))
        {
            if (ChainOf(strip) is { } other && other != keep)
                SelectOnly(other, null);
        }
    }

    private static void Add(MenuFlyout menu, string key, Action action, bool isEnabled = true)
    {
        MenuItem item = new() { Header = Loc.Tr(key), IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static int AfterLast(ICommandChain chain, IEnumerable<CommandSegment> selected) =>
        selected.Select(s => chain.Commands.IndexOf(s)).DefaultIfEmpty(chain.Commands.Count - 1).Max() + 1;

    private static void CopyAll(ICommandChain chain, ICommandClipboardService clipboard) =>
        clipboard.Copy(chain.Commands.Select(s => s.Raw));

    private static void Remove(ICommandChain chain, IEnumerable<CommandSegment> segments)
    {
        foreach (CommandSegment segment in segments.ToList())
            chain.RemoveSegment(segment);
    }

    /// <summary>Inserts <paramref name="commands"/> at <paramref name="index"/> in their order and
    /// makes them the selection, so a paste can be copied or moved on right away.</summary>
    private static void Insert(ICommandChain chain, IReadOnlyList<string> commands, int index)
    {
        SelectOnly(chain, null);
        foreach (string raw in commands)
        {
            if (!chain.InsertRawAt(raw, index)) continue;
            chain.Commands[index].IsSelected = true;
            index++;
        }
    }
}
