using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Services;
using LoupixDeck.Services.Folders;

namespace LoupixDeck.Commands;

// ── Custom folders (issue #249) ──────────────────────────────────────────────────────
// Folder navigation changes bound editor state, so every command marshals onto the UI thread.

[Command(FolderCommand.OpenName, "Open Folder", "Folders",
    parameterTemplate: "({Folder})",
    parameterNames: ["Folder"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Open a folder of the active workspace")]
public class OpenFolderCommand(IPageManager pageManager) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !Guid.TryParse(parameters[0], out var id))
        {
            Console.WriteLine($"Usage: {FolderCommand.OpenName}(folderId)");
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => pageManager.OpenFolder(id, FolderOpenMode.Push));
    }
}

[Command(FolderCommand.BackName, "Folder Back", "Folders",
    Description = "Close the innermost open folder")]
public class FolderBackCommand(IPageManager pageManager) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(pageManager.FolderBack);
    }
}

[Command(FolderCommand.CloseName, "Close All Folders", "Folders",
    Description = "Close every open folder and return to the page")]
public class CloseFoldersCommand(IPageManager pageManager) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(pageManager.CloseFolders);
    }
}
