using System.Reflection;
using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Commands;

/// <summary>Resolved presentation metadata for a command group (category) card.</summary>
public sealed class GroupInfo
{
    /// <summary>Section the category is filed under in the picker.</summary>
    public CommandGroupSection Section { get; init; } = CommandGroupSection.Plugins;

    /// <summary>MDI glyph (a single UTF-32 code point as a string) shown on the card.</summary>
    public string Icon { get; init; }

    /// <summary>Short description shown under the category title on the card.</summary>
    public string Description { get; init; }
}

/// <summary>
/// Resolves a command group name to its card metadata (section, icon, description).
/// Core groups declare it via assembly-level <see cref="CommandGroupAttribute"/>;
/// plugins via <see cref="LoupixPlugin.GetCommandGroups"/>. Undeclared groups fall
/// back to the <see cref="CommandGroupSection.Plugins"/> section with a generic icon.
/// </summary>
public interface IGroupCatalog
{
    GroupInfo Resolve(string groupName);
}

/// <inheritdoc cref="IGroupCatalog"/>
public sealed class GroupCatalog : IGroupCatalog
{
    /// <summary>mdi-puzzle — the generic fallback icon for an undeclared group.</summary>
    private const string FallbackIcon = "\U000F0431";

    private static readonly GroupInfo Fallback = new()
    {
        Section = CommandGroupSection.Plugins,
        Icon = FallbackIcon,
        Description = null
    };

    private readonly IPluginManager _pluginManager;
    private readonly Lock _sync = new();
    private Dictionary<string, GroupInfo> _coreGroups;

    public GroupCatalog(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public GroupInfo Resolve(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            return Fallback;

        EnsureCoreGroups();
        if (_coreGroups.TryGetValue(groupName, out var core))
            return core;

        return ResolvePluginGroup(groupName) ?? Fallback;
    }

    private void EnsureCoreGroups()
    {
        if (_coreGroups != null)
            return;

        lock (_sync)
        {
            if (_coreGroups != null)
                return;

            var dict = new Dictionary<string, GroupInfo>(StringComparer.Ordinal);
            foreach (var attr in Assembly.GetExecutingAssembly().GetCustomAttributes<CommandGroupAttribute>())
            {
                dict[attr.Group] = new GroupInfo
                {
                    Section = attr.Section,
                    Icon = string.IsNullOrEmpty(attr.Icon) ? FallbackIcon : attr.Icon,
                    Description = attr.Description
                };
            }

            _coreGroups = dict;
        }
    }

    private GroupInfo ResolvePluginGroup(string groupName)
    {
        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded || plugin.Instance == null)
                continue;

            IReadOnlyList<CommandGroupDescriptor> groups;
            try
            {
                groups = plugin.Instance.GetCommandGroups();
            }
            catch
            {
                // A faulty plugin must not break group resolution for others.
                continue;
            }

            if (groups == null)
                continue;

            foreach (var group in groups)
            {
                if (group != null && string.Equals(group.Group, groupName, StringComparison.Ordinal))
                    return new GroupInfo
                    {
                        Section = group.Section,
                        Icon = string.IsNullOrEmpty(group.Icon) ? FallbackIcon : group.Icon,
                        Description = group.Description
                    };
            }
        }

        return null;
    }
}
