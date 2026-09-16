using System.Security.Cryptography;
using System.Text;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.DialPresets;

/// <summary>
/// The dial presets the plugins enabled on this device contribute through
/// <see cref="LoupixPlugin.GetDialPresets"/>.
/// </summary>
public interface IPluginDialPresetSource
{
    /// <summary>Every contributed preset that can actually be applied here, in plugin order.</summary>
    IReadOnlyList<DialPreset> Presets { get; }
}

/// <inheritdoc cref="IPluginDialPresetSource"/>
/// <remarks>
/// <para>
/// Descriptors are materialised into ordinary <see cref="DialPreset"/> instances with their command
/// strings already built, so every consumer — the quick menu, the panel row, the drag and drop, the
/// catalogue entry — keeps working on the one shape it already knows. Applying one stays a one-shot
/// copy: nothing on the dial points back at the plugin afterwards.
/// </para>
/// <para>
/// Per device, and gated on the device's enabled set exactly like
/// <see cref="Commands.PluginMenuContributor"/>: plugins are loaded once for the whole process, so
/// a plugin's presets must not be offered on a device that has it switched off (issue #163).
/// </para>
/// <para>
/// Built on every read rather than cached. A plugin's preset list is allowed to depend on live
/// state — one preset per audio endpoint, say — and a cache would pin whatever was true when the
/// plugin loaded, so a device plugged in afterwards would never show up. The lists are built when
/// the user opens a preset surface, not on a timer, and the SDK asks contributors to return
/// promptly for exactly this reason.
/// </para>
/// </remarks>
public sealed class PluginDialPresetSource : IPluginDialPresetSource
{
    private readonly IPluginManager _pluginManager;
    private readonly ICommandBuilder _commandBuilder;
    private readonly ICommandRegistry _registry;
    private readonly LoupedeckConfig _config;

    public PluginDialPresetSource(IPluginManager pluginManager, ICommandBuilder commandBuilder,
        ICommandRegistry registry, LoupedeckConfig config)
    {
        _pluginManager = pluginManager;
        _commandBuilder = commandBuilder;
        _registry = registry;
        _config = config;
    }

    public IReadOnlyList<DialPreset> Presets => Build();

    private List<DialPreset> Build()
    {
        List<DialPreset> presets = [];

        foreach (LoadedPlugin plugin in _pluginManager?.Plugins ?? [])
        {
            if (plugin.Status != PluginLoadStatus.Loaded || plugin.Instance == null)
                continue;

            string pluginId = plugin.Manifest?.Id;
            if (!EnabledForDevice(pluginId))
                continue;

            string pluginName = plugin.Manifest?.Name ?? pluginId;

            foreach (DialPresetDescriptor descriptor in SafeGetDescriptors(plugin))
            {
                DialPreset preset = Convert(descriptor, pluginId, pluginName, presets);
                if (preset != null)
                    presets.Add(preset);
            }
        }

        return presets;
    }

    /// <summary>True when this device's config enables the plugin with the given id.</summary>
    private bool EnabledForDevice(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        List<string> enabled = _config?.EnabledPlugins;
        return enabled != null
               && enabled.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The plugin's descriptors, or none when it throws. A faulty contribution must cost the plugin
    /// its presets, not its load.
    /// </summary>
    private static IReadOnlyList<DialPresetDescriptor> SafeGetDescriptors(LoadedPlugin plugin)
    {
        try
        {
            return [.. plugin.Instance.GetDialPresets() ?? []];
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"PluginDialPresetSource: '{plugin.Manifest?.Id}' failed to list dial presets: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// One descriptor as a preset, or null when it cannot be offered: no id or name, an id this
    /// plugin already used, no command that resolves, or a command this device cannot put on a
    /// dial.
    /// </summary>
    private DialPreset Convert(DialPresetDescriptor descriptor, string pluginId, string pluginName,
        List<DialPreset> taken)
    {
        if (descriptor == null ||
            string.IsNullOrWhiteSpace(descriptor.Id) ||
            string.IsNullOrWhiteSpace(descriptor.Name))
            return null;

        Guid id = DeriveId(pluginId, descriptor.Id);
        if (taken.Any(p => p.Id == id))
            return null;

        Dictionary<RotaryAction, string> commands =
            RotaryGroupBuilder.Build(descriptor.Actions, _commandBuilder);

        DialPreset preset = new()
        {
            Id = id,
            Name = descriptor.Name.Trim(),
            Left = commands.GetValueOrDefault(RotaryAction.CounterClockwise, string.Empty),
            Right = commands.GetValueOrDefault(RotaryAction.Clockwise, string.Empty),
            Press = commands.GetValueOrDefault(RotaryAction.Press, string.Empty),
            Glyph = string.IsNullOrEmpty(descriptor.Glyph) ? DialPreset.DefaultGlyph : descriptor.Glyph,
            SourcePluginId = pluginId,
            SourcePluginName = pluginName
        };

        if (preset.IsEmpty || !DialPresetCommandFilter.IsUsable(preset, _registry))
            return null;

        return preset;
    }

    /// <summary>
    /// A stable id for "this preset of this plugin". The SDK's descriptor id is a string the plugin
    /// owns, while the rest of the app addresses a preset by <see cref="Guid"/>; deriving one from
    /// the other keeps the preset the same object across restarts without asking plugin authors for
    /// a GUID. Name-based UUID (RFC 4122 §4.3) over a fixed namespace.
    /// </summary>
    private static Guid DeriveId(string pluginId, string descriptorId)
    {
        // Fixed namespace for LoupixDeck plugin dial presets — never change it, or every
        // contributed preset changes identity.
        ReadOnlySpan<byte> ns =
        [
            0x6a, 0x1f, 0x0b, 0x4e, 0xff, 0xff, 0x4f, 0x5a,
            0x9c, 0x21, 0x7d, 0x3b, 0x8e, 0x5a, 0x10, 0xff
        ];

        byte[] name = Encoding.UTF8.GetBytes($"{pluginId}/{descriptorId}");
        byte[] buffer = new byte[ns.Length + name.Length];
        ns.CopyTo(buffer);
        name.CopyTo(buffer, ns.Length);

        byte[] hash = SHA1.HashData(buffer);
        byte[] bytes = hash[..16];

        // Version 5, RFC 4122 variant.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Guid's first three fields are little-endian on this platform; the hash is big-endian.
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        return new Guid(bytes);
    }
}
