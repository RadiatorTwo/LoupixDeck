using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.StarterProfiles;

/// <summary>A plugin whose commands a starter profile uses.</summary>
/// <param name="Id">Plugin id as in <c>plugin.json</c> and the Plugin Store catalog.</param>
/// <param name="Name">Name shown when offering to install it.</param>
public sealed record StarterPluginRequirement(string Id, string Name);

/// <summary>
/// A ready-made profile shipped with the app (issue #301). It is built for the attached device on
/// demand, so one template fits every device model, and shows new users what a profile can do.
/// </summary>
public abstract class StarterProfileTemplate
{
    /// <summary>Stable id, never shown.</summary>
    public abstract string Id { get; }

    /// <summary>Localization key of the template's name, which also names the created profile.</summary>
    protected abstract string NameKey { get; }

    /// <summary>Localization key of the one-line description shown in the picker.</summary>
    protected abstract string DescriptionKey { get; }

    /// <summary>Localization key of what to set up after creating the profile, or null.</summary>
    protected virtual string SetupNoteKey => null;

    /// <summary>Material Design icon shown next to the template in the picker.</summary>
    public abstract string SymbolId { get; }

    public string Name => Loc.Tr(NameKey);

    public string Description => Loc.Tr(DescriptionKey);

    public string SetupNote => SetupNoteKey == null ? null : Loc.Tr(SetupNoteKey);

    public string Glyph => SymbolLibrary.TryGet(SymbolId, out SymbolDefinition definition) ? definition.Glyph : string.Empty;

    /// <summary>Plugins the template uses on this platform.</summary>
    public virtual IReadOnlyList<StarterPluginRequirement> GetRequiredPlugins(bool isWindows) => [];

    /// <summary>Plugin names for the picker, or an empty string when the template needs none.</summary>
    public string RequiredPluginsText(bool isWindows) =>
        string.Join(", ", GetRequiredPlugins(isWindows).Select(plugin => plugin.Name));

    /// <summary>Fills <paramref name="profile"/>, which has no workspaces yet.</summary>
    public abstract void Build(StarterProfileBuilder builder, Profile profile);
}