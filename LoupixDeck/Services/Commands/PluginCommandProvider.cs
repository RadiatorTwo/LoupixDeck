using LoupixDeck.Commands.Base;
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

    public PluginCommandProvider(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IEnumerable<RegisteredCommand> GetCommands()
    {
        var result = new List<RegisteredCommand>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
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

    private static RegisteredCommand Adapt(IPluginCommand command, IPluginHost host)
    {
        var descriptor = command.Descriptor;

        var info = new CommandInfo
        {
            CommandName = descriptor.CommandName,
            DisplayName = descriptor.DisplayName,
            Group = descriptor.Group,
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
        Func<string[], string> getText = null;
        Func<string[], IRenderCanvas, bool> renderImage = null;
        Func<string[], IRenderCanvas, AnimationFrameContext, AnimationFrameInfo> renderAnimatedFrame = null;

        CommandContext DisplayContext(string[] parameters) => new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = ButtonTargets.TouchButton,
            Device = host?.ActiveDevice,
            Host = host
        };

        // Classification precedence: animated → image → text. A command implementing several picks
        // the richest path only, so exactly one render loop drives it.
        // The animated path is driven by the central scheduler (button-animation engine), not the
        // UpdateInterval poll, so it sets neither IsDisplayCommand nor IsImageDisplayCommand.
        if (command is IAnimatedDisplayCommand animatedCommand)
        {
            isAnimatedImage = true;
            animatedFps = animatedCommand.TargetFps;
            renderAnimatedFrame = (parameters, canvas, frame) =>
                animatedCommand.RenderAnimatedFrame(DisplayContext(parameters), canvas, frame);
        }
        else if (command is IDisplayImageCommand imageCommand)
        {
            isImageDisplay = true;
            interval = imageCommand.UpdateInterval;
            renderImage = (parameters, canvas) => imageCommand.RenderImage(DisplayContext(parameters), canvas);
        }
        else if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = parameters => displayCommand.GetText(DisplayContext(parameters));
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
