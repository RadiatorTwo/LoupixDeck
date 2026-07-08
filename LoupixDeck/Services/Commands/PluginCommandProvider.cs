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
                    result.Add(Adapt(command, plugin.Host));
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

    private static RegisteredCommand Adapt(IPluginCommand command, IPluginHost host)
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
                .Select(p => new ParameterDescriptor(p.Name, p.ParameterType))
                .ToList()
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

        var isDisplay = false;
        var isImageDisplay = false;
        var isAnimatedImage = false;
        var animatedFps = 0;
        var interval = TimeSpan.Zero;
        Func<string[], IReadOnlyList<SequenceCommand>, string> getText = null;
        Func<string[], IReadOnlyList<SequenceCommand>, IRenderCanvas, bool> renderImage = null;
        Func<string[], IReadOnlyList<SequenceCommand>, IRenderCanvas, AnimationFrameContext, AnimationFrameInfo> renderAnimatedFrame = null;

        CommandContext DisplayContext(string[] parameters, IReadOnlyList<SequenceCommand> sequence) => new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = ButtonTargets.TouchButton,
            Device = host?.ActiveDevice,
            Host = host,
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
            renderAnimatedFrame = (parameters, sequence, canvas, frame) =>
                animatedCommand.RenderAnimatedFrame(DisplayContext(parameters, sequence), canvas, frame);
        }
        else if (command is IDisplayImageCommand imageCommand)
        {
            isImageDisplay = true;
            interval = imageCommand.UpdateInterval;
            renderImage = (parameters, sequence, canvas) => imageCommand.RenderImage(DisplayContext(parameters, sequence), canvas);
        }
        else if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = (parameters, sequence) => displayCommand.GetText(DisplayContext(parameters, sequence));
        }

        return new RegisteredCommand
        {
            CommandName = descriptor.CommandName,
            Info = info,
            SupportedTargets = command.SupportedTargets,
            HiddenFromMenu = descriptor.HiddenFromMenu,
            IsDisplayCommand = isDisplay,
            IsImageDisplayCommand = isImageDisplay,
            IsAnimatedImageCommand = isAnimatedImage,
            AnimatedTargetFps = animatedFps,
            UpdateInterval = interval,
            Execute = execute,
            GetText = getText,
            RenderImage = renderImage,
            RenderAnimatedFrame = renderAnimatedFrame
        };
    }
}
