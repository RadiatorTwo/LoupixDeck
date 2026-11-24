using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Localization;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Utils;

/// <summary>
/// Right-click menu of the command sequence strips in the button editors (issue #684). On a chip it
/// copies, cuts, duplicates or removes that command, or pastes after it; on the strip itself it
/// pastes at the end. Either way it can copy the whole sequence. A copied command keeps the values
/// the user set, so it lands on another button, gesture or device exactly as configured.
///
/// Attached once per editor window: the event bubbles up from the chip, so no per-chip wiring is
/// needed. A strip is any <c>Border.sequence-strip</c>; its chain is the view model behind the chip
/// list inside it.
/// </summary>
public static class CommandChainMenu
{
    public static void Attach(Window window) =>
        window.AddHandler(InputElement.ContextRequestedEvent, OnContextRequested);

    private static void OnContextRequested(object sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || e.Source is not Visual source) return;

        Border strip = source.GetSelfAndVisualAncestors().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("sequence-strip"));
        if (strip == null) return;

        ICommandChain chain = strip.GetVisualDescendants().OfType<ItemsControl>()
            .Select(list => list.DataContext).OfType<ICommandChain>().FirstOrDefault();
        ICommandClipboardService clipboard = Program.AppServices?.GetService<ICommandClipboardService>();
        if (chain == null || clipboard == null) return;

        CommandSegment segment = source.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != strip)
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<CommandSegment>()
            .FirstOrDefault();

        MenuFlyout menu = new();

        if (segment != null)
        {
            bool hasRaw = !string.IsNullOrWhiteSpace(segment.Raw);

            Add(menu, "ButtonMenu_Copy", () => clipboard.Copy([segment.Raw]), hasRaw);
            Add(menu, "ButtonMenu_Cut", () =>
            {
                clipboard.Copy([segment.Raw]);
                chain.RemoveSegment(segment);
            }, hasRaw);
            Add(menu, "CommandMenu_PasteAfter",
                () => Paste(chain, clipboard, chain.Commands.IndexOf(segment) + 1), clipboard.HasData);
            Add(menu, "CommandMenu_Duplicate",
                () => chain.InsertRawAt(segment.Raw, chain.Commands.IndexOf(segment) + 1), hasRaw);
            Add(menu, "CommandMenu_Remove", () => chain.RemoveSegment(segment));

            // With a single chip the whole sequence is that chip, which Copy already covers.
            if (chain.Commands.Count > 1)
            {
                menu.Items.Add(new Separator());
                Add(menu, "CommandMenu_CopyAll", () => CopyAll(chain, clipboard));
            }
        }
        else
        {
            if (chain.Commands.Count > 0)
                Add(menu, "CommandMenu_CopyAll", () => CopyAll(chain, clipboard));
            // The strip of an empty sequence offers the paste even with nothing copied, so a
            // right-click there always answers with something.
            Add(menu, "ButtonMenu_Paste", () => Paste(chain, clipboard, chain.Commands.Count), clipboard.HasData);
        }

        menu.ShowAt(strip, showAtPointer: true);
        e.Handled = true;
    }

    private static void Add(MenuFlyout menu, string key, Action action, bool isEnabled = true)
    {
        MenuItem item = new() { Header = Loc.Tr(key), IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void CopyAll(ICommandChain chain, ICommandClipboardService clipboard) =>
        clipboard.Copy(chain.Commands.Select(s => s.Raw));

    /// <summary>Inserts the copied commands at <paramref name="index"/>, keeping their order.</summary>
    private static void Paste(ICommandChain chain, ICommandClipboardService clipboard, int index)
    {
        foreach (string raw in clipboard.Commands)
        {
            if (chain.InsertRawAt(raw, index))
                index++;
        }
    }
}
