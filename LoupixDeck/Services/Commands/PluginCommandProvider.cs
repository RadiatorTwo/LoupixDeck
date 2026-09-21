using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Feeds the <see cref="ICommandRegistry"/> with commands contributed by loaded
/// plugins, adapting each <see cref="IPluginCommand"/> to a
/// <see cref="RegisteredCommand"/>.
/// </summary>
public class PluginCommandProvider : ICommandProvider
{
    private readonly IPluginManager _pluginManager;
    private readonly LoupedeckConfig _config;

    public PluginCommandProvider(IPluginManager pluginManager, LoupedeckConfig config)
    {
        _pluginManager = pluginManager;
        _config = config;
    }

    public IEnumerable<RegisteredCommand> GetCommands()
    {
        var result = new List<RegisteredCommand>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
                continue;

            // Plugins load once and are shared across devices (union enable-gate), but this
            // provider feeds a per-device registry. Filter to the plugins THIS device has
            // enabled, so a command contributed by a plugin another device enabled does not
            // become assignable/executable here (issue #163).
            if (!PluginEnabledForDevice(plugin.Manifest?.Id))
                continue;

            foreach (var command in plugin.Commands)
            {
                try
                {
                    result.Add(Adapt(command, plugin.Host, plugin.Manifest?.Id));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"PluginCommandProvider: '{plugin.Manifest?.Id}' command adapt failed: {ex.Message}");
                }
            }
        }

        return result;
    }

    /// <summary>True when this device's config enables the plugin with the given id.</summary>
    private bool PluginEnabledForDevice(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        var enabled = _config?.EnabledPlugins;
        return enabled != null
               && enabled.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private static RegisteredCommand Adapt(IPluginCommand command, IPluginHost host, string pluginId)
    {
        var descriptor = command.Descriptor;

        var info = new CommandInfo
        {
            CommandName = descriptor.CommandName,
            DisplayName = descriptor.DisplayName,
            Group = descriptor.Group,
            Icon = descriptor.Icon,
            Description = descriptor.Description,
            ParameterTemplate = descriptor.ParameterTemplate,
            Parameters = descriptor.Parameters
                .Select(p => new ParameterDescriptor(p.Name, p.ParameterType, p.DefaultValue))
                .ToList(),
            States = descriptor.States
                .Select(state => new CommandStateInfo(state.Name, state.Description))
                .ToList(),
            OwnerPluginId = pluginId
        };

        Func<string[], ButtonTargets, int?, Task> execute = async (parameters, target, sourceIndex) =>
        {
            try
            {
                await command.Execute(new CommandContext
                {
                    Parameters = parameters ?? Array.Empty<string>(),
                    Target = target,
                    SourceIndex = sourceIndex,
                    Device = host?.ActiveDevice,
                    Host = host
                });
            }
            catch (Exception ex)
            {
                // Without this, an Execute exception bubbles up to the
                // button-press handler with no plugin attribution.
                host?.Logger?.Error($"Execute failed for '{descriptor.CommandName}'", ex);
            }
        };

        // Rotary value adjustment (IAdjustmentCommand). Orthogonal to the display
        // classification below: the same command may also render its own button.
        var isAdjustment = false;
        Func<string[], int?, int, Task> applyAdjustment = null;
        Func<string[], int?, Task> applyReset = null;
        Func<string[], int?, string> getValueText = null;

        CommandContext RotaryContext(string[] parameters, int? sourceIndex) => new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = ButtonTargets.RotaryEncoder,
            SourceIndex = sourceIndex,
            Device = host?.ActiveDevice,
            Host = host
        };

        if (command is IAdjustmentCommand adjustmentCommand)
        {
            isAdjustment = true;

            applyAdjustment = async (parameters, sourceIndex, ticks) =>
            {
                try
                {
                    await adjustmentCommand.ApplyAdjustment(RotaryContext(parameters, sourceIndex), ticks);
                }
                catch (Exception ex)
                {
                    host?.Logger?.Error($"ApplyAdjustment failed for '{descriptor.CommandName}'", ex);
                }
            };

            applyReset = async (parameters, sourceIndex) =>
            {
                try
                {
                    await adjustmentCommand.ApplyReset(RotaryContext(parameters, sourceIndex));
                }
                catch (Exception ex)
                {
                    host?.Logger?.Error($"ApplyReset failed for '{descriptor.CommandName}'", ex);
                }
            };

            getValueText = (parameters, sourceIndex) =>
            {
                try
                {
                    return adjustmentCommand.GetValueText(RotaryContext(parameters, sourceIndex));
                }
                catch (Exception ex)
                {
                    // Runs on the strip render path — a throwing plugin must not take the
                    // whole strip down, so the dial falls back to its static label.
                    host?.Logger?.Error($"GetValueText failed for '{descriptor.CommandName}'", ex);
                    return null;
                }
            };
        }

        var isDisplay = false;
        var isImageDisplay = false;
        var isAnimatedImage = false;
        var animatedFps = 0;
        var interval = TimeSpan.Zero;
        Func<string[], IReadOnlyList<SequenceCommand>, string, string> getText = null;
        Func<string[], IReadOnlyList<SequenceCommand>, string, IRenderCanvas, bool> renderImage = null;
        Func<string[], IReadOnlyList<SequenceCommand>, string, IRenderCanvas, AnimationFrameContext, AnimationFrameInfo>
            renderAnimatedFrame = null;

        CommandContext DisplayContext(string[] parameters, IReadOnlyList<SequenceCommand> sequence, string stateName) => new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = ButtonTargets.TouchButton,
            Device = host?.ActiveDevice,
            Host = host,
            StateName = stateName,
            SequenceCommands = sequence ?? []
        };

        // Classification precedence: animated → image → text. A command implementing several picks
        // the richest path only, so exactly one render loop drives it.
        // The animated path is driven by the central scheduler (button-animation engine), not the
        // UpdateInterval poll, so it sets neither IsDisplayCommand nor IsImageDisplayCommand.
        if (command is IAnimatedDisplayCommand animatedCommand)
        {
            isAnimatedImage = true;
            animatedFps = animatedCommand.TargetFps;
            renderAnimatedFrame = (parameters, sequence, stateName, canvas, frame) =>
                animatedCommand.RenderAnimatedFrame(DisplayContext(parameters, sequence, stateName), canvas, frame);
        }
        else if (command is IDisplayImageCommand imageCommand)
        {
            isImageDisplay = true;
            interval = imageCommand.UpdateInterval;
            renderImage = (parameters, sequence, stateName, canvas) =>
                imageCommand.RenderImage(DisplayContext(parameters, sequence, stateName), canvas);
        }
        else if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = (parameters, sequence, stateName) =>
                displayCommand.GetText(DisplayContext(parameters, sequence, stateName));
        }

        return new RegisteredCommand
        {
            CommandName = descriptor.CommandName,
            Info = info,
            SupportedTargets = command.SupportedTargets,
            HiddenFromMenu = descriptor.HiddenFromMenu,
            States = info.States,
            IsDisplayCommand = isDisplay,
            IsImageDisplayCommand = isImageDisplay,
            IsAnimatedImageCommand = isAnimatedImage,
            AnimatedTargetFps = animatedFps,
            UpdateInterval = interval,
            IsAdjustmentCommand = isAdjustment,
            ApplyAdjustment = applyAdjustment,
            ApplyReset = applyReset,
            GetValueText = getValueText,
            Execute = execute,
            GetText = getText,
            RenderImage = renderImage,
            RenderAnimatedFrame = renderAnimatedFrame
        };
    }
}