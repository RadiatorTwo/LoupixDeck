using System.Text;
using LoupixDeck.Localization;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Resolves a check id to its localized title. The orchestrator needs this to name a check
/// that threw or timed out, i.e. exactly when the check itself could not supply a title.
/// Keeping it here lets <see cref="ILinuxDiagnosticCheck"/> stay at the three members the
/// issue specifies.
/// </summary>
public static class DiagnosticCheckTitles
{
    /// <summary>
    /// The localized title for <paramref name="id"/>. "uinput.write-access" resolves through
    /// the key "Diagnostics_Check_UinputWriteAccess", so a new check needs no table entry here,
    /// only its key in the string files.
    /// </summary>
    public static string For(string id) => Loc.Tr(KeyFor(id));

    /// <summary>The localization key a check id maps to.</summary>
    public static string KeyFor(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "Diagnostics_Check_Unnamed";
        }

        StringBuilder key = new("Diagnostics_Check_");
        bool capitalize = true;

        foreach (char character in id)
        {
            if ((character == '.') || (character == '-') || (character == '_'))
            {
                capitalize = true;
                continue;
            }

            key.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        return key.ToString();
    }
}
