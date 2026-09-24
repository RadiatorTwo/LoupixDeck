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

        Func<string[], ButtonTargets, int?, string, Task> execute = async (parameters, target, sourceIndex, buttonKey) =>
        {
            try
            {
                await command.Execute(new CommandContext
                {
                    Parameters = parameters ?? Array.Empty<string>(),
                    Target = target,
                    SourceIndex = sourceIndex,
                    Device = host?.ActiveDevice,
                    Host = host,
                    ButtonKey = buttonKey
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
        Func<string[], int?, AdjustmentValue?> getValue = null;

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

            getValue = (parameters, sourceIndex) =>
            {
                CommandContext ctx = RotaryContext(parameters, sourceIndex);
                try
                {
                    AdjustmentValue? value = adjustmentCommand.GetValue(ctx);
                    if (value.HasValue)
                    {
                        // A plugin computing its own scale may land marginally outside it; that is
                        // a rounding artefact, not a reason to skip the indicator.
                        return value.Value with { Normalized = Math.Clamp(value.Value.Normalized, 0d, 1d) };
                    }

                    // A command that supplies only text keeps working. NaN is the host's marker
                    // for "no scale position" — a caption without a bar. Normalized 0 would mean
                    // the opposite: a bar the plugin claims is empty.
                    string text = adjustmentCommand.GetValueText(ctx);
                    return string.IsNullOrWhiteSpace(text) ? null : new AdjustmentValue(double.NaN, text);
                }
                catch (Exception ex)
                {
                    // Runs on the strip render path — a throwing plugin must not take the
                    // whole strip down, so the dial falls back to its static label.
                    host?.Logger?.Error($"GetValue failed for '{descriptor.CommandName}'", ex);
                    return null;
                }
            };
        }

        var isDisplay = false;
        var isImageDisplay = false;
        var isAnimatedImage = false;
        var animatedFps = 0;
        var interval = TimeSpan.Zero;
        Func<string[], IReadOnlyList<SequenceCommand>, string, string, string> getText = null;
        Func<string[], IReadOnlyList<SequenceCommand>, string, string, IRenderCanvas, bool> renderImage = null;
        Func<string[], IReadOnlyList<SequenceCommand>, string, string, IRenderCanvas, AnimationFrameContext, AnimationFrameInfo>
            renderAnimatedFrame = null;

        CommandContext DisplayContext(string[] parameters, IReadOnlyList<SequenceCommand> sequence, string stateName,
            string buttonKey) => new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = ButtonTargets.TouchButton,
            Device = host?.ActiveDevice,
            Host = host,
            StateName = stateName,
            SequenceCommands = sequence ?? [],
            ButtonKey = buttonKey
        };

        // Classification precedence: animated → image → text. A command implementing several picks
        // the richest path only, so exactly one render loop drives it.
        // The animated path is driven by the central scheduler (button-animation engine), not the
        // UpdateInterval poll, so it sets neither IsDisplayCommand nor IsImageDisplayCommand.
        if (command is IAnimatedDisplayCommand animatedCommand)
        {
            isAnimatedImage = true;
            animatedFps = animatedCommand.TargetFps;
            renderAnimatedFrame = (parameters, sequence, stateName, buttonKey, canvas, frame) =>
                animatedCommand.RenderAnimatedFrame(DisplayContext(parameters, sequence, stateName, buttonKey), canvas, frame);
        }
        else if (command is IDisplayImageCommand imageCommand)
        {
            isImageDisplay = true;
            interval = imageCommand.UpdateInterval;
            renderImage = (parameters, sequence, stateName, buttonKey, canvas) =>
                imageCommand.RenderImage(DisplayContext(parameters, sequence, stateName, buttonKey), canvas);
        }
        else if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = (parameters, sequence, stateName, buttonKey) =>
                displayCommand.GetText(DisplayContext(parameters, sequence, stateName, buttonKey));
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
            GetValue = getValue,
            Execute = execute,
            GetText = getText,
            RenderImage = renderImage,
            RenderAnimatedFrame = renderAnimatedFrame
        };
    }
}