using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LoupixDeck.Services.History;

/// <summary>
/// Undo/redo of the edits made in the main window (issue #622). Every edit there ends in a config
/// save, so each save is compared with the content recorded at the previous one; what changed
/// becomes one step. The recorded content is the active workspace (pages, folders) and the active
/// profile's round LED buttons, which belong to the profile rather than to a workspace. Undo and
/// redo go back to the profile and workspace a step was made in and put its content back.
/// </summary>
public interface IWorkspaceHistoryService
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>Raised after <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    event Action Changed;

    /// <summary>Records what changed since the last capture as one step. Called on every config
    /// save, on the UI thread.</summary>
    void Capture();

    /// <summary>Takes the active workspace's content as the starting point without recording a
    /// step. Called after a workspace was activated, on the UI thread.</summary>
    void TakeBaseline();

    Task Undo();
    Task Redo();

    /// <summary>The asset files the recorded steps use, which the save-time asset cleanup has to
    /// keep so an undone step gets its images back.</summary>
    IEnumerable<string> ReferencedAssetPaths();
}

public sealed class WorkspaceHistoryService(LoupedeckConfig config, IServiceProvider services) : IWorkspaceHistoryService
{
    /// <summary>One recorded content. <see cref="Comparable"/> leaves out which state each button
    /// shows, a runtime value that a press changes and that is not an edit.</summary>
    private sealed record Snapshot(string Content, string Comparable, IReadOnlyList<string> Assets);

    /// <summary>What a snapshot covers: a workspace, or with an empty workspace id the profile's
    /// LED buttons.</summary>
    private readonly record struct Scope(Guid ProfileId, Guid WorkspaceId)
    {
        public bool IsLedButtons => WorkspaceId == Guid.Empty;
    }

    private sealed record Part(Scope Scope, string Before, string After, IReadOnlyList<string> Assets);

    private sealed record Step(IReadOnlyList<Part> Parts);

    private readonly UndoStack<Step> _stack = new();
    private readonly Dictionary<Scope, Snapshot> _baselines = [];
    private bool _restoring;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    public event Action Changed
    {
        add => _stack.Changed += value;
        remove => _stack.Changed -= value;
    }

    public void Capture()
    {
        if (_restoring) return;

        List<Part> parts = [];
        foreach ((Scope scope, Snapshot now) in CaptureActive())
        {
            if (_baselines.TryGetValue(scope, out Snapshot before) && before.Comparable != now.Comparable)
                parts.Add(new Part(scope, before.Content, now.Content, [.. before.Assets.Union(now.Assets)]));
            _baselines[scope] = now;
        }

        if (parts.Count > 0)
            _stack.Push(new Step(parts));
    }

    public void TakeBaseline()
    {
        if (_restoring) return;

        foreach ((Scope scope, Snapshot now) in CaptureActive())
            _baselines[scope] = now;
    }

    public async Task Undo()
    {
        if (_restoring) return;

        // An edit whose save has not been captured yet is the newest step.
        Capture();
        Step step = _stack.Undo();
        if (step != null)
            await Restore(step, undo: true);
    }

    public async Task Redo()
    {
        if (_restoring) return;

        // An edit made after the undo drops the redo side, as in any editor.
        Capture();
        Step step = _stack.Redo();
        if (step != null)
            await Restore(step, undo: false);
    }

    public IEnumerable<string> ReferencedAssetPaths()
        => _stack.Steps.SelectMany(static s => s.Parts).SelectMany(static p => p.Assets).Distinct();

    private IEnumerable<(Scope, Snapshot)> CaptureActive()
    {
        Profile profile = config.ActiveProfile;
        Workspace workspace = config.ActiveWorkspace;
        if (profile == null || workspace == null) yield break;

        yield return (new Scope(profile.Id, workspace.Id), SnapshotOf(workspace));
        yield return (new Scope(profile.Id, Guid.Empty), SnapshotOf(profile));
    }

    private static Snapshot SnapshotOf(Workspace workspace)
        => SnapshotOf(JObject.FromObject(workspace, ButtonSnapshot.Serializer));

    private static Snapshot SnapshotOf(Profile profile)
        => SnapshotOf(JArray.FromObject(profile.SimpleButtons ?? [], ButtonSnapshot.Serializer));

    private static Snapshot SnapshotOf(JContainer json)
    {
        string content = json.ToString(Formatting.None);
        List<string> assets = [.. AssetPathHarvester.Harvest(json).Distinct()];

        foreach (JProperty activeState in json.Descendants().OfType<JProperty>()
                     .Where(static p => p.Name == nameof(StatefulButton.ActiveStateId)).ToList())
            activeState.Remove();

        return new Snapshot(content, json.ToString(Formatting.None), assets);
    }

    private async Task Restore(Step step, bool undo)
    {
        _restoring = true;
        try
        {
            await GoTo(step);

            IDeviceController controller = services.GetRequiredService<IDeviceController>();
            IPageManager pages = controller.PageManager;
            Workspace active = config.ActiveWorkspace;

            // Where the user was, so the undo does not throw them back to the startup page.
            int touchIndex = active?.CurrentTouchPageIndex ?? -1;
            int rotaryIndex = active?.CurrentRotaryPageIndex ?? -1;
            int leftIndex = active?.CurrentLeftRotaryPageIndex ?? -1;
            int rightIndex = active?.CurrentRightRotaryPageIndex ?? -1;
            List<Guid> folderPath = [.. active?.FolderPath.Select(static f => f.Id) ?? []];

            bool activeWorkspaceChanged = false;
            bool activeLedsChanged = false;

            foreach (Part part in step.Parts)
            {
                Profile profile = config.Profiles?.FirstOrDefault(p => p.Id == part.Scope.ProfileId);
                if (profile == null) continue;

                string content = undo ? part.Before : part.After;

                if (part.Scope.IsLedButtons)
                {
                    SimpleButton[] buttons = JArray.Parse(content).ToObject<SimpleButton[]>(ButtonSnapshot.Serializer);
                    foreach (SimpleButton button in buttons)
                        button?.RewireAfterLoad();
                    profile.SimpleButtons = buttons;
                    activeLedsChanged |= ReferenceEquals(profile, config.ActiveProfile);
                    _baselines[part.Scope] = SnapshotOf(profile);
                    continue;
                }

                Workspace workspace = profile.Workspaces?.FirstOrDefault(w => w.Id == part.Scope.WorkspaceId);
                if (workspace == null) continue;

                if (ReferenceEquals(workspace, active))
                {
                    // The open folder's pages are about to be replaced; leave them first.
                    if (active.IsFolderOpen)
                        await pages.CloseFolders();
                    activeWorkspaceChanged = true;
                }

                ApplyWorkspace(workspace, content);
                _baselines[part.Scope] = SnapshotOf(workspace);
            }

            if (activeLedsChanged)
                await controller.ApplyActiveProfileButtons();

            if (activeWorkspaceChanged)
            {
                // Same refresh as a workspace switch, so everything bound to the workspace (folder
                // panel, page selectors) reads the new pages and folders.
                config.RebindActiveWorkspace();
                await controller.ApplyActiveWorkspace();
                await ReturnTo(pages, touchIndex, rotaryIndex, leftIndex, rightIndex, folderPath);
            }

            controller.SaveConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorkspaceHistory] Restoring a step failed: {ex}");
        }
        finally
        {
            _restoring = false;
        }

        // The pages and buttons now in the active workspace are the starting point of the next step.
        TakeBaseline();
    }

    /// <summary>Activates the profile and workspace the step was made in, so the user sees what
    /// the undo changes. A companion device follows its master and stays where it is.</summary>
    private async Task GoTo(Step step)
    {
        Scope scope = step.Parts.Select(static p => p.Scope).FirstOrDefault(static s => !s.IsLedButtons, step.Parts[0].Scope);
        Profile profile = config.Profiles?.FirstOrDefault(p => p.Id == scope.ProfileId);
        if (profile == null) return;

        IWorkspaceActivationService activation = services.GetRequiredService<IWorkspaceActivationService>();
        if (config.ActiveProfileId != profile.Id)
            await activation.ActivateProfile(profile.Id);

        if (!scope.IsLedButtons && config.ActiveWorkspaceId != scope.WorkspaceId
            && profile.Workspaces?.Any(w => w.Id == scope.WorkspaceId) == true)
            await activation.ActivateWorkspace(scope.WorkspaceId);
    }

    /// <summary>
    /// Puts recorded content into <paramref name="workspace"/> in place. Every property the config
    /// writes is copied except the id, so the workspace object, which the selectors and services
    /// hold on to, stays the same, and a property added to the model later is covered too.
    /// </summary>
    private static void ApplyWorkspace(Workspace workspace, string content)
    {
        Workspace recorded = JObject.Parse(content).ToObject<Workspace>(ButtonSnapshot.Serializer);
        if (recorded == null) return;

        var contract = (JsonObjectContract)ButtonSnapshot.Serializer.ContractResolver.ResolveContract(typeof(Workspace));
        foreach (JsonProperty property in contract.Properties)
        {
            if (property.Ignored || !property.Readable || !property.Writable
                || property.PropertyName == nameof(Workspace.Id))
                continue;

            property.ValueProvider!.SetValue(workspace, property.ValueProvider.GetValue(recorded));
        }

        // The JSON converters bypass the handlers that make a layer edit repaint its button, the
        // same as after loading the config.
        foreach (TouchButtonPage page in workspace.EnumerateTouchLayouts())
        {
            foreach (RotaryButton mode in page.WheelModes ?? [])
                mode?.RewireAfterLoad();
            foreach (TouchButton button in page.TouchButtons ?? [])
                button?.RewireLayerHandlers();
        }

        foreach (RotaryButtonPage page in workspace.RotaryButtonPages
                     .Concat(workspace.LeftRotaryButtonPages)
                     .Concat(workspace.RightRotaryButtonPages))
        {
            foreach (RotaryButton button in page?.RotaryButtons ?? [])
                button?.RewireAfterLoad();
        }
    }

    /// <summary>Shows the pages (and the folder) that were showing before the restore, where they
    /// still exist.</summary>
    private async Task ReturnTo(IPageManager pages, int touchIndex, int rotaryIndex, int leftIndex, int rightIndex,
        IReadOnlyList<Guid> folderPath)
    {
        Workspace workspace = config.ActiveWorkspace;
        if (workspace == null) return;

        if (touchIndex >= 0 && touchIndex < workspace.TouchButtonPages.Count
                            && touchIndex != workspace.CurrentTouchPageIndex)
            await pages.ApplyTouchPage(touchIndex);

        if (pages.HasIndependentRotarySides)
        {
            if (leftIndex >= 0 && leftIndex < workspace.LeftRotaryButtonPages.Count)
                pages.ApplyRotaryPage(RotarySide.Left, leftIndex);
            if (rightIndex >= 0 && rightIndex < workspace.RightRotaryButtonPages.Count)
                pages.ApplyRotaryPage(RotarySide.Right, rightIndex);
        }
        else if (rotaryIndex >= 0 && rotaryIndex < workspace.RotaryButtonPages.Count)
        {
            pages.ApplyRotaryPage(RotarySide.Both, rotaryIndex);
        }

        if (folderPath.Count > 0 && folderPath.All(id => workspace.EnumerateFolders().Any(f => f.Id == id)))
            await pages.SetFolderPath(folderPath);
    }
}
