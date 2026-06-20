namespace LoupixDeck.PluginSdk;

/// <summary>
/// The editor kind the host renders for a plugin setting, and the value type
/// it is stored as in <see cref="IPluginSettings"/>.
/// </summary>
public enum PluginSettingKind
{
    /// <summary>Single-line text. Stored as <see cref="string"/>.</summary>
    Text,

    /// <summary>Masked text (e.g. a password). Stored as <see cref="string"/>.</summary>
    Password,

    /// <summary>Integer number. Stored as a JSON integer (read with <c>Get&lt;long&gt;</c>).</summary>
    Number,

    /// <summary>On/off switch. Stored as <see cref="bool"/>.</summary>
    Toggle,

    /// <summary>
    /// Display-only heading/separator used to group fields. Has no editor and
    /// stores no value — only <c>Label</c> (and optionally <c>Description</c>)
    /// are read by the host. <c>Key</c> must still be unique within the schema.
    /// </summary>
    Heading
}
