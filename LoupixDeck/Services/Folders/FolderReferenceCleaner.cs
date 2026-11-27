using LoupixDeck.Models;

namespace LoupixDeck.Services.Folders;

/// <summary>
/// Removes links to folders that no longer exist (issue #249), so a deleted folder never leaves a
/// button behind that navigates nowhere. Static so the companion structure sync and the package
/// importer can run it on workspaces that are not the active one.
/// </summary>
public static class FolderReferenceCleaner
{
    /// <summary>
    /// Strips every command segment that opens one of <paramref name="folderIds"/> from the
    /// workspace's touch layouts, rotary pages and wraps, and from <paramref name="ledButtons"/>.
    /// A button whose only content was such a link is cleared entirely, label and glyph included.
    /// Returns the number of buttons and wraps changed.
    /// </summary>
    public static int Clean(Workspace workspace, IReadOnlySet<Guid> folderIds, IEnumerable<SimpleButton> ledButtons = null)
    {
        if (workspace == null || folderIds == null || folderIds.Count == 0) return 0;

        int changed = 0;

        foreach (TouchButtonPage layout in workspace.EnumerateTouchLayouts())
        {
            foreach (TouchButton button in layout.TouchButtons)
                if (button != null && CleanStateful(button, folderIds, ButtonContentReset.ClearTouchContent))
                    changed++;

            if (CleanWrap(layout.TouchButtonWrap, folderIds))
                changed++;
        }

        foreach (RotaryButtonPage page in (workspace.RotaryButtonPages ?? [])
                     .Concat(workspace.LeftRotaryButtonPages ?? [])
                     .Concat(workspace.RightRotaryButtonPages ?? []))
        {
            if (page == null) continue;
            changed += CleanRotaryPage(page, folderIds);
        }

        foreach (SimpleButton button in ledButtons ?? [])
            if (button != null && CleanStateful(button, folderIds, ButtonContentReset.ClearSimpleContent))
                changed++;

        return changed;
    }

    /// <summary>
    /// Cleans links that point at folders missing from the workspace, for content that came from
    /// elsewhere (an imported page, a companion mirror whose tree changed).
    /// </summary>
    public static int CleanDangling(Workspace workspace, IEnumerable<SimpleButton> ledButtons = null)
    {
        if (workspace == null) return 0;

        HashSet<Guid> existing = [.. workspace.EnumerateFolders().Select(static f => f.Id)];
        HashSet<Guid> dangling = [];

        void Collect(CommandBinding command)
        {
            foreach (Guid id in FolderCommand.ReferencedFolders(command))
                if (!existing.Contains(id))
                    dangling.Add(id);
        }

        foreach (TouchButtonPage layout in workspace.EnumerateTouchLayouts())
        {
            foreach (TouchButton button in layout.TouchButtons)
                foreach (ButtonState state in button?.States ?? [])
                    Collect(state?.Command);
            Collect(layout.TouchButtonWrap?.PreCommands);
            Collect(layout.TouchButtonWrap?.PostCommands);
        }

        foreach (RotaryButtonPage page in (workspace.RotaryButtonPages ?? [])
                     .Concat(workspace.LeftRotaryButtonPages ?? [])
                     .Concat(workspace.RightRotaryButtonPages ?? []))
        {
            foreach (RotaryButton button in page?.RotaryButtons ?? [])
            {
                Collect(button?.Command);
                Collect(button?.RotaryLeftCommand);
                Collect(button?.RotaryRightCommand);
            }

            foreach (CommandBinding command in page?.StripSegmentCommands ?? [])
                Collect(command);

            foreach (CommandWrap wrap in WrapsOf(page))
            {
                Collect(wrap?.PreCommands);
                Collect(wrap?.PostCommands);
            }
        }

        foreach (SimpleButton button in ledButtons ?? [])
            foreach (ButtonState state in button?.States ?? [])
                Collect(state?.Command);

        return dangling.Count == 0 ? 0 : Clean(workspace, dangling, ledButtons);
    }

    /// <summary>
    /// Strips the folder links <paramref name="reject"/> refuses from a single button, for content that
    /// just arrived on it (a paste). Returns true when the button changed.
    /// </summary>
    public static bool CleanButton(StatefulButton button, Func<Guid, bool> reject)
    {
        if (button?.States == null || reject == null) return false;

        HashSet<Guid> rejected = [.. button.States
            .SelectMany(static s => FolderCommand.ReferencedFolders(s?.Command))
            .Where(reject)];
        if (rejected.Count == 0) return false;

        return button switch
        {
            TouchButton touch => CleanStateful(touch, rejected, ButtonContentReset.ClearTouchContent),
            SimpleButton simple => CleanStateful(simple, rejected, ButtonContentReset.ClearSimpleContent),
            _ => false
        };
    }

    private static bool CleanStateful<TButton>(TButton button, IReadOnlySet<Guid> folderIds, Action<TButton> clear)
        where TButton : StatefulButton
    {
        if (button.States == null) return false;

        bool changed = false;
        foreach (ButtonState state in button.States)
        {
            if (state == null || !FolderCommand.ReferencedFolders(state.Command).Any(folderIds.Contains)) continue;

            CommandBinding remaining = FolderCommand.RemoveReferences(state.Command, folderIds);
            if (CommandBinding.IsNullOrEmpty(remaining) && button.States.Count == 1)
            {
                clear(button);
                return true;
            }

            if (ReferenceEquals(state, button.ActiveState))
                button.Command = remaining;
            else
                state.Command = remaining;
            changed = true;
        }

        if (changed)
            button.Refresh();
        return changed;
    }

    private static int CleanRotaryPage(RotaryButtonPage page, IReadOnlySet<Guid> folderIds)
    {
        int changed = 0;

        foreach (RotaryButton button in page.RotaryButtons ?? [])
        {
            if (button == null) continue;
            bool touched = false;

            CommandBinding command = Strip(button.Command, folderIds, ref touched);
            CommandBinding left = Strip(button.RotaryLeftCommand, folderIds, ref touched);
            CommandBinding right = Strip(button.RotaryRightCommand, folderIds, ref touched);
            if (!touched) continue;

            button.Command = command;
            button.RotaryLeftCommand = left ?? CommandBinding.Empty;
            button.RotaryRightCommand = right ?? CommandBinding.Empty;
            button.Refresh();
            changed++;
        }

        if (page.StripSegmentCommands != null)
        {
            for (int i = 0; i < page.StripSegmentCommands.Length; i++)
            {
                bool touched = false;
                CommandBinding command = Strip(page.StripSegmentCommands[i], folderIds, ref touched);
                if (!touched) continue;
                page.StripSegmentCommands[i] = command;
                changed++;
            }
        }

        foreach (CommandWrap wrap in WrapsOf(page))
            if (CleanWrap(wrap, folderIds))
                changed++;

        return changed;
    }

    private static IEnumerable<CommandWrap> WrapsOf(RotaryButtonPage page)
        => page == null ? [] : [page.SimpleButtonWrap, page.KnobLeftWrap, page.KnobRightWrap, page.KnobPressWrap];

    private static bool CleanWrap(CommandWrap wrap, IReadOnlySet<Guid> folderIds)
    {
        if (wrap == null) return false;

        bool touched = false;
        CommandBinding pre = Strip(wrap.PreCommands, folderIds, ref touched);
        CommandBinding post = Strip(wrap.PostCommands, folderIds, ref touched);
        if (!touched) return false;

        wrap.PreCommands = pre ?? CommandBinding.Empty;
        wrap.PostCommands = post ?? CommandBinding.Empty;
        return true;
    }

    private static CommandBinding Strip(CommandBinding command, IReadOnlySet<Guid> folderIds, ref bool touched)
    {
        if (!FolderCommand.ReferencedFolders(command).Any(folderIds.Contains)) return command;
        touched = true;
        return FolderCommand.RemoveReferences(command, folderIds);
    }
}
