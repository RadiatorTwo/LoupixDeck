using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.Commands;
using LoupixDeck.ViewModels.ActionPanel;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// Writes a row from the main window's side panel onto a device button. The panel offers two kinds
/// of row — an installed application and a catalogue command — and three kinds of target, so this
/// is where the two are matched up.
/// </summary>
public interface IPanelAssignmentService
{
    /// <summary>
    /// True when <paramref name="item"/> can be put on <paramref name="target"/>. Commands declare
    /// the button types they support, and a target the command does not support is rejected here
    /// rather than silently writing a command the button can never run.
    /// </summary>
    bool CanAssign(PanelItemViewModel item, LoupedeckButton target);

    /// <summary>
    /// Puts <paramref name="item"/> on <paramref name="target"/>, replacing whatever was there.
    /// Returns false when nothing was written — an unsupported combination, or an application whose
    /// launch command could not be built. The caller owns saving and repainting.
    /// </summary>
    Task<bool> AssignAsync(PanelItemViewModel item, LoupedeckButton target);
}

/// <inheritdoc cref="IPanelAssignmentService"/>
public sealed class PanelAssignmentService(
    ICommandBuilder commandBuilder,
    ICommandRegistry commandRegistry,
    ICommandStateMaterializer stateMaterializer,
    IAssetService assetService,
    IAppIconExtractor appIcons,
    IDeviceService deviceService,
    DeviceGeometry geometry) : IPanelAssignmentService
{
    private readonly DeviceGeometry _geometry = geometry ?? DeviceGeometry.Default;

    public bool CanAssign(PanelItemViewModel item, LoupedeckButton target)
    {
        ButtonTargets? kind = TargetKind(target);
        if (item == null || kind == null)
            return false;

        return item switch
        {
            // An application is a plain launch command, which every button type can run.
            AppPanelItemViewModel => true,
            // A rotary group fills a dial's three actions at once and means nothing anywhere else.
            ActionPanelItemViewModel { Entry.IsCommandGroup: true } => kind == ButtonTargets.RotaryEncoder,
            ActionPanelItemViewModel action => Supports(action, kind.Value),
            _ => false
        };
    }

    public async Task<bool> AssignAsync(PanelItemViewModel item, LoupedeckButton target)
    {
        if (!CanAssign(item, target))
            return false;

        return item switch
        {
            AppPanelItemViewModel app => await AssignAppAsync(app, target),
            ActionPanelItemViewModel action => AssignAction(action, target),
            _ => false
        };
    }

    // ── Applications ───────────────────────────────────────────────────────

    private async Task<bool> AssignAppAsync(AppPanelItemViewModel item, LoupedeckButton target)
    {
        InstalledApp app = item.App;
        string command = AppAssignment.BuildLaunchCommand(app);
        if (string.IsNullOrEmpty(command))
            return false;

        if (target is not TouchButton touch)
            return AssignCommandOnly(command, app.Name, target);

        // Resolve and import the icon before the button is touched, so a failed extraction cannot
        // leave it half-applied — the same order the button editor's app assignment uses.
        string relative = null;
        try
        {
            string iconFile = await appIcons.GetIconFileAsync(app);
            if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
                relative = assetService.Import(iconFile);
        }
        catch (Exception ex)
        {
            // Without an icon the button still launches the application, so this is not fatal.
            Console.WriteLine($"[ActionPanel] Icon for '{app.Name}' could not be imported: {ex.Message}");
        }

        Models.Layers.ImageLayer layer = AppAssignment.ApplyToTouchButton(
            touch, app, relative, replaceLayers: true, layerName: app.Name, keySizePx: _geometry.KeySize);

        if (layer != null)
            layer.CachedImage = assetService.Load(relative);

        ReconcileStates(touch);
        return true;
    }

    // ── Catalogue commands ─────────────────────────────────────────────────

    private bool AssignAction(ActionPanelItemViewModel item, LoupedeckButton target)
    {
        if (item.Entry.RotaryGroup is { Count: > 0 } group)
            return ApplyRotaryGroup(group, (RotaryButton)target, item.Title);

        string command = commandBuilder.CreateCommandFromMenuEntry(item.Entry);
        if (string.IsNullOrEmpty(command))
            return false;

        if (target is not TouchButton touch)
            return AssignCommandOnly(command, item.Title, target);

        ActionAssignment.ApplyToTouchButton(touch, command, item.Title, item.SymbolId,
            _geometry.KeySize, _geometry.KeySize);

        ReconcileStates(touch);
        return true;
    }

    /// <summary>
    /// Fills a dial's three actions from one catalogue entry. An action the group does not name
    /// leaves its slot as it was, which is what the button editor does with the same entry.
    /// </summary>
    private static bool ApplyRotaryGroup(IReadOnlyDictionary<RotaryAction, string> group,
        RotaryButton dial, string label)
    {
        foreach ((RotaryAction action, string command) in group)
        {
            switch (action)
            {
                case RotaryAction.CounterClockwise:
                    dial.RotaryLeftCommand = command;
                    break;
                case RotaryAction.Clockwise:
                    dial.RotaryRightCommand = command;
                    break;
                case RotaryAction.Press:
                    dial.Command = command;
                    break;
            }
        }

        dial.DisplayText = label ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Assigns to a button that has no artwork of its own: an LED button, or a dial's press action.
    /// A dial also gets the name as its side-strip label, which is the only place its assignment is
    /// visible on the hardware.
    /// </summary>
    private bool AssignCommandOnly(string command, string label, LoupedeckButton target)
    {
        switch (target)
        {
            case SimpleButton simple:
                simple.Command = command;
                ReconcileStates(simple);
                return true;

            case RotaryButton rotary:
                rotary.Command = command;
                rotary.DisplayText = label ?? string.Empty;
                return true;

            default:
                return false;
        }
    }

    // ── Shared ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the states a newly assigned command declares, or hands back the ones the replaced
    /// command owned.
    /// </summary>
    /// <remarks>
    /// A released state set is always kept, never discarded. The editor asks the user that question,
    /// but this path runs at the end of a drag or a click with no dialog in flight, and keeping the
    /// states is the answer that loses nothing — they stay as ordinary editable states.
    /// </remarks>
    private void ReconcileStates(StatefulButton button)
    {
        if (stateMaterializer.Reconcile(button) != StateSyncResult.ReleaseRequested)
            return;

        stateMaterializer.Release(button, keepStates: true);
        stateMaterializer.Reconcile(button);
    }

    private bool Supports(ActionPanelItemViewModel item, ButtonTargets kind)
    {
        RegisteredCommand command = commandRegistry.Get(item.Entry.Command);

        // A command the registry does not know cannot be validated; the command builder will
        // produce nothing for it, so let the assignment itself fail rather than guessing here.
        return command == null || command.SupportedTargets.HasFlag(kind);
    }

    /// <summary>
    /// The command-target flag a button corresponds to. A side display is not a key — its content
    /// lives on the rotary page's strip canvas — so it is deliberately not a panel drop target.
    /// </summary>
    private ButtonTargets? TargetKind(LoupedeckButton button) => button switch
    {
        TouchButton touch => IsSideDisplay(touch) ? null : ButtonTargets.TouchButton,
        SimpleButton => ButtonTargets.SimpleButton,
        RotaryButton => ButtonTargets.RotaryEncoder,
        _ => null
    };

    /// <summary>
    /// Slots 12 and 13 are the side displays on a device that has them, and an ordinary key on a
    /// device that does not — the same test the clipboard and drag machine use.
    /// </summary>
    private bool IsSideDisplay(TouchButton touch) =>
        (deviceService.Device?.HasSideStrips == true) &&
        touch.Index is LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex
            or LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex;
}
