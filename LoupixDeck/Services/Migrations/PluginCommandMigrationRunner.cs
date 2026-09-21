using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Applies the <see cref="CommandMigration"/> rules of the loaded plugins to a device's rotary
/// bindings. Distinct from <see cref="IConfigMigration"/>: that chain upgrades the config
/// <em>format</em> and is owned by the host, while these rules rewrite <em>content</em> and are
/// owned by whichever plugin replaced its own commands.
/// </summary>
public interface IPluginCommandMigrationRunner
{
    /// <summary>
    /// Runs every rule that has not run against this config yet. Returns true when the config
    /// was changed and saved. Touches nothing when no rule is pending, and leaves the config
    /// exactly as it was when anything fails.
    /// </summary>
    bool Apply(LoupedeckConfig config, string configPath);
}

public sealed class PluginCommandMigrationRunner(
    IPluginManager pluginManager,
    ICommandRegistry commandRegistry,
    ICommandBuilder commandBuilder,
    IConfigService configService) : IPluginCommandMigrationRunner
{
    public bool Apply(LoupedeckConfig config, string configPath)
    {
        if (config == null) return false;

        List<(string Id, CommandMigration Rule)> pending = CollectPendingRules(config);
        if (pending.Count == 0)
            return false;

        // Planned first, applied second: nothing is mutated until the backup exists, so a
        // failure anywhere above leaves the user's bindings untouched.
        List<(RotaryButton Dial, RotaryAction Action, string Command)> edits = [];

        Dictionary<string, int> rewritten = new(StringComparer.Ordinal);

        foreach (RotaryButton dial in Dials(config))
        {
            foreach ((string id, CommandMigration rule) in pending)
            {
                if (!TryBuildReplacement(dial, rule, out string replacement))
                    continue;

                foreach (RotaryAction action in rule.From.Keys)
                    edits.Add((dial, action, replacement));

                rewritten[id] = rewritten.GetValueOrDefault(id) + 1;

                // One rule per dial: a dial that matched is no longer the shape a later rule
                // is looking for.
                break;
            }
        }

        if (edits.Count == 0)
        {
            // Still record the rules: they ran, they found nothing, and re-running them later
            // would rewrite a binding the user has rebuilt by hand in the meantime.
            config.AppliedCommandMigrations.AddRange(pending.Select(p => p.Id));
            TrySave(config, configPath);
            return false;
        }

        if (!TryBackup(configPath))
            return false;

        foreach ((RotaryButton dial, RotaryAction action, string command) in edits)
            dial.SetCommand(action, command);

        config.AppliedCommandMigrations.AddRange(pending.Select(p => p.Id));

        foreach ((string id, int count) in rewritten)
            Console.WriteLine($"[Migration] '{id}' rewrote {count} dial(s).");

        return TrySave(config, configPath);
    }

    /// <summary>
    /// The rules of every plugin this device has enabled that have not run against this config,
    /// keyed by their qualified id. A rule whose target command is not registered is dropped —
    /// rewriting a binding onto a command that does not exist would be data loss.
    /// </summary>
    private List<(string Id, CommandMigration Rule)> CollectPendingRules(LoupedeckConfig config)
    {
        List<(string, CommandMigration)> result = [];
        HashSet<string> seen = new(config.AppliedCommandMigrations ?? [], StringComparer.Ordinal);

        foreach (LoadedPlugin plugin in pluginManager.Plugins ?? [])
        {
            string pluginId = plugin?.Manifest?.Id;
            if (plugin?.Status != PluginLoadStatus.Loaded || string.IsNullOrWhiteSpace(pluginId))
                continue;

            if (config.EnabledPlugins?.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) != true)
                continue;

            IEnumerable<CommandMigration> rules;
            try
            {
                rules = plugin.Instance?.GetCommandMigrations() ?? [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Migration] '{pluginId}' GetCommandMigrations failed: {ex.Message}");
                continue;
            }

            foreach (CommandMigration rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.To))
                    continue;

                if (rule.From == null || rule.From.Count == 0)
                    continue;

                string qualified = $"{pluginId}:{rule.Id}";
                if (!seen.Add(qualified))
                    continue;

                if (commandRegistry.Get(rule.To) == null)
                {
                    Console.WriteLine($"[Migration] '{qualified}' skipped: '{rule.To}' is not registered.");
                    seen.Remove(qualified);
                    continue;
                }

                result.Add((qualified, rule));
            }
        }

        return result;
    }

    /// <summary>Every dial of every page of every workspace of every profile.</summary>
    private static IEnumerable<RotaryButton> Dials(LoupedeckConfig config)
    {
        foreach (Profile profile in config.Profiles ?? [])
        {
            foreach (Workspace workspace in profile?.Workspaces ?? [])
            {
                if (workspace == null) continue;

                IEnumerable<RotaryButtonPage> pages =
                    (workspace.RotaryButtonPages ?? []).AsEnumerable()
                    .Concat(workspace.LeftRotaryButtonPages ?? [])
                    .Concat(workspace.RightRotaryButtonPages ?? []);

                foreach (RotaryButtonPage page in pages)
                {
                    foreach (RotaryButton dial in page?.RotaryButtons ?? [])
                    {
                        if (dial != null) yield return dial;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decides whether a dial carries exactly the rule's old binding and, if so, builds the
    /// command string that replaces it. Every gesture the rule names must hold that command on
    /// its own — a chain, a different command or an empty slot means the user's dial is not the
    /// one the rule describes, and it is left alone.
    /// </summary>
    private bool TryBuildReplacement(RotaryButton dial, CommandMigration rule, out string replacement)
    {
        replacement = null;

        // Parameter values of the matched slots, by the parameter name the OLD command declares
        // for that position. Collected in gesture order so a value only one of the old commands
        // carried (a step the mute command never had) is still found.
        Dictionary<string, string> oldValues = new(StringComparer.Ordinal);

        foreach (RotaryAction action in RotaryButtonExtensions.Gestures)
        {
            string binding = dial.GetCommand(action);

            if (!rule.From.TryGetValue(action, out string expected))
            {
                // A gesture the rule does not mention must be free, otherwise the dial is a
                // composition the user built and not the shape the plugin is replacing.
                if (!string.IsNullOrWhiteSpace(binding)) return false;
                continue;
            }

            List<string> parts = CommandStringParser.SplitChain(binding)?.ToList() ?? [];
            if (parts.Count != 1) return false;

            if (!string.Equals(CommandStringParser.GetName(parts[0]), expected, StringComparison.Ordinal))
                return false;

            if (!CollectParameters(parts[0], expected, oldValues))
                return false;
        }

        CommandInfo target = commandRegistry.Get(rule.To)?.Info;
        if (target == null) return false;

        Dictionary<string, object> values = new(StringComparer.Ordinal);
        foreach (ParameterDescriptor parameter in target.Parameters ?? [])
        {
            if (TryResolveParameter(parameter, rule, oldValues, out string value))
                values[parameter.Name] = value;
        }

        replacement = commandBuilder.BuildCommandString(target, values);
        return !string.IsNullOrWhiteSpace(replacement);
    }

    /// <summary>
    /// Records one matched slot's parameter values under the old command's parameter names.
    /// Returns false when a name already seen carries a different value — two gestures pointing
    /// at different targets are not one dial the rule can replace.
    /// </summary>
    private bool CollectParameters(string binding, string commandName, Dictionary<string, string> into)
    {
        CommandInfo info = commandRegistry.Get(commandName)?.Info;
        if (info == null) return false;

        string[] parameters = CommandStringParser.GetParameters(binding) ?? [];

        for (int i = 0; i < (info.Parameters?.Count ?? 0) && i < parameters.Length; i++)
        {
            string name = info.Parameters[i].Name;
            string value = parameters[i];
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (into.TryGetValue(name, out string existing))
            {
                if (!string.Equals(existing, value, StringComparison.Ordinal)) return false;
                continue;
            }

            into[name] = value;
        }

        return true;
    }

    /// <summary>
    /// The value for one parameter of the new command: a literal from the rule, a value carried
    /// over from the old binding when the rule wrote <c>"{oldName}"</c>, or nothing — then the
    /// builder applies the parameter's own default.
    /// </summary>
    private static bool TryResolveParameter(
        ParameterDescriptor parameter,
        CommandMigration rule,
        IReadOnlyDictionary<string, string> oldValues,
        out string value)
    {
        value = null;

        if (rule.Parameters?.TryGetValue(parameter.Name, out string source) != true)
            return false;

        if (string.IsNullOrEmpty(source))
            return false;

        if (source.Length > 2 && source[0] == '{' && source[^1] == '}')
        {
            string oldName = source[1..^1];
            return oldValues.TryGetValue(oldName, out value) && !string.IsNullOrWhiteSpace(value);
        }

        value = source;
        return true;
    }

    /// <summary>
    /// Copies the config next to itself before the only write. Failing here aborts the whole
    /// migration: a rewrite the user cannot undo is worse than a dial that stays on its old
    /// commands, which keep working.
    /// </summary>
    private static bool TryBackup(string configPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                return true;

            string backup = $"{configPath}.premigration.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            if (File.Exists(backup)) return true;

            File.Copy(configPath, backup);
            Console.WriteLine($"[Migration] config backed up to '{backup}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Migration] backup failed, nothing was migrated: {ex.Message}");
            return false;
        }
    }

    private bool TrySave(LoupedeckConfig config, string configPath)
    {
        try
        {
            configService.SaveConfig(config, configPath);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Migration] saving the migrated config failed: {ex.Message}");
            return false;
        }
    }
}
