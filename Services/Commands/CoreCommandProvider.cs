using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Feeds the <see cref="ICommandRegistry"/> with the app's built-in commands by
/// wrapping the unchanged reflection-based <see cref="ISysCommandService"/>.
/// </summary>
public class CoreCommandProvider : ICommandProvider
{
    private readonly ISysCommandService _sysCommandService;
    private readonly IServiceProvider _serviceProvider;
    private bool _scanned;

    public CoreCommandProvider(ISysCommandService sysCommandService, IServiceProvider serviceProvider)
    {
        _sysCommandService = sysCommandService;
        _serviceProvider = serviceProvider;
    }

    public IEnumerable<RegisteredCommand> GetCommands()
    {
        // The reflection scan only needs to run once.
        if (!_scanned)
        {
            _sysCommandService.Initialize();
            _scanned = true;
        }

        var result = new List<RegisteredCommand>();

        foreach (var info in _sysCommandService.GetCommandInfos())
        {
            var name = info.CommandName;

            var isDisplay = false;
            var interval = TimeSpan.Zero;
            Func<string[], string> getText = null;

            if (_sysCommandService.TryGetCommandType(name, out var type)
                && typeof(IDynamicTextProvider).IsAssignableFrom(type))
            {
                try
                {
                    // Display providers are stateless w.r.t. their per-call
                    // parameters, so a single shared instance per command is
                    // sufficient (and cheaper than one per button).
                    var provider = (IDynamicTextProvider)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                    isDisplay = true;
                    interval = provider.UpdateInterval;
                    getText = provider.GetText;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CoreCommandProvider: failed to instantiate display command '{name}': {ex.Message}");
                }
            }

            var capturedName = name;
            result.Add(new RegisteredCommand
            {
                CommandName = name,
                Info = info,
                SupportedTargets = ResolveTargets(name, info.Group),
                IsDisplayCommand = isDisplay,
                UpdateInterval = interval,
                Execute = (parameters, _) => _sysCommandService.ExecuteCommand(capturedName, parameters),
                GetText = getText
            });
        }

        return result;
    }

    /// <summary>
    /// Maps a core command to the button types its group is offered on. This
    /// replaces the per-ViewModel hard-coded menu composition: the same mapping
    /// now drives every button type's command-selection menu.
    /// </summary>
    private static ButtonTargets ResolveTargets(string commandName, string group)
    {
        // Wakeup only makes sense from an external trigger (CLI, hotkey) — a
        // device button cannot fire it while the device is off.
        if (commandName == "System.DeviceWakeup")
            return ButtonTargets.None;

        return group switch
        {
            "Pages" => ButtonTargets.All,
            "Device Control" => ButtonTargets.All,
            "Macros" => ButtonTargets.TouchButton,
            "Dynamic Text" => ButtonTargets.TouchButton,
            // "Button Control" and anything unmapped is not menu-assignable.
            _ => ButtonTargets.None
        };
    }
}
