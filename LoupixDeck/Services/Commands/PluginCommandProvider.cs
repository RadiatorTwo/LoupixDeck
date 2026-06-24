#nullable enable
using System.Collections.Immutable;
using System.Diagnostics;
using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Feeds the <see cref="ICommandRegistry"/> with commands contributed by loaded
/// plugins, adapting each <see cref="IPluginCommand"/> to a
/// <see cref="RegisteredCommand"/>.
/// </summary>
public class PluginCommandProvider(IPluginManager pluginManager) : ICommandProvider
{
    public IEnumerable<RegisteredCommand> GetCommands()
    {
        var result = new List<RegisteredCommand>(pluginManager.Plugins.Count);

        foreach (var plugin in pluginManager.Plugins)
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

    private static CommandContext CreateCommandContext(string[] parameters, ButtonTargets target, PluginHost? host, int? sourceIndex)
    {
        Debug.Assert(host is not null, "Why do we not have a host?");
        Debug.Assert(parameters is not null, "parameters should not be null; use Array.Empty<string>() instead");
        return new()
        {
            Parameters = parameters ?? Array.Empty<string>(),
            Target = target,
            SourceIndex = sourceIndex,
            Device = host?.ActiveDevice,
            Host = host
        };
    }

    private static CommandContext CreateDisplayContext(string[] parameters, PluginHost? host)
        => CreateCommandContext(parameters, ButtonTargets.None, host, null);

    // Minor private delegate wrapper to enforce/call out what is and is not captured into the closure.
    private static CommandExecutionDelegate GetExecuteDelegate(IPluginCommand command, PluginHost? host, CommandDescriptor descriptor)
    {
        return async (parameters, target, sourceIndex) =>
        {
            Debug.Assert(parameters is not null, "parameters should not be null; use Array.Empty<string>() instead");
            try
            {
                CommandContext ctx = CreateCommandContext(parameters, target, host, sourceIndex);
                await command.Execute(ctx);
            }
            catch (Exception ex)
            {
                // Without this, an Execute exception bubbles up to the
                // button-press handler with no plugin attribution.
                host?.Logger?.Error($"Execute failed for '{descriptor.CommandName}'", ex);
            }
        };
    }

    private static RegisteredCommand Adapt(IPluginCommand command, PluginHost? host)
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
                .ToImmutableArray()
        };

        CommandExecutionDelegate execute = GetExecuteDelegate(command, host, descriptor);

        var isDisplay = false;
        var isImageDisplay = false;
        var isAnimatedImage = false;
        var animatedFps = 0;
        var interval = TimeSpan.Zero;
        Func<string[], string>? getText = null;
        Func<string[], IRenderCanvas, bool>? renderImage = null;
        Func<string[], IRenderCanvas, AnimationFrameContext, AnimationFrameInfo>? renderAnimatedFrame = null;

        // Classification precedence: animated → image → text. A command implementing several picks
        // the richest path only, so exactly one render loop drives it.
        // The animated path is driven by the central scheduler (button-animation engine), not the
        // UpdateInterval poll, so it sets neither IsDisplayCommand nor IsImageDisplayCommand.
        if (command is IAnimatedDisplayCommand animatedCommand)
        {
            isAnimatedImage = true;
            animatedFps = animatedCommand.TargetFps;
            renderAnimatedFrame = (parameters, canvas, frame) =>
                animatedCommand.RenderAnimatedFrame(CreateDisplayContext(parameters, host), canvas, frame);
        }
        else if (command is IDisplayImageCommand imageCommand)
        {
            isImageDisplay = true;
            interval = imageCommand.UpdateInterval;
            renderImage = (parameters, canvas) => imageCommand.RenderImage(CreateDisplayContext(parameters, host), canvas);
        }
        else if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = parameters => displayCommand.GetText(CreateDisplayContext(parameters, host));
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
