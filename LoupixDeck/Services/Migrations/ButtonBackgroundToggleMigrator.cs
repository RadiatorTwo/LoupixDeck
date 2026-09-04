using Avalonia.Media;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v10 → v11: makes a button state's background explicit.
///
/// A page wallpaper used to win unconditionally — a touch button's <c>BackColor</c> was drawn only
/// when the page had none, so a color picked on a wallpaper page did nothing. The new
/// <c>BackgroundEnabled</c> flag decides instead: on, the color covers the wallpaper; off, the key
/// stays bare.
///
/// Old files carry no flag, so this migration derives it from what the state actually shows today:
/// a state that still has the untouched black default never displayed a color of its own and is
/// migrated to "off" (identical rendering — a bare key is black on a page without a wallpaper, and
/// on one with a wallpaper the wallpaper keeps showing). A state with any other color had picked
/// one deliberately and is migrated to "on", so that choice now also holds on a wallpaper page.
/// </summary>
public sealed class ButtonBackgroundToggleMigrator : IConfigMigration
{
    public int FromVersion => 10;

    /// <summary>Rotary page collections whose strip canvas is a touch button too.</summary>
    private static readonly string[] RotaryPageKeys =
    [
        "RotaryButtonPages",
        "LeftRotaryButtonPages",
        "RightRotaryButtonPages"
    ];

    public void Apply(JObject root, string configFilePath)
    {
        foreach (JObject workspace in Workspaces(root))
        {
            if (workspace["TouchButtonPages"] is JArray touchPages)
            {
                foreach (JObject page in touchPages.OfType<JObject>())
                {
                    if (page["TouchButtons"] is JArray buttons)
                    {
                        foreach (JObject button in buttons.OfType<JObject>())
                            MigrateButton(button);
                    }
                }
            }

            foreach (string key in RotaryPageKeys)
            {
                if (workspace[key] is not JArray rotaryPages) continue;

                foreach (JObject page in rotaryPages.OfType<JObject>())
                {
                    if (page["StripCanvas"] is JObject strip)
                        MigrateButton(strip);
                }
            }
        }

        root["Version"] = FromVersion + 1;
    }

    /// <summary>
    /// Every workspace of every profile, plus the config root itself — a file that predates
    /// profiles has already been lifted into one by <see cref="ProfilesWorkspacesMigrator"/>,
    /// but the root is checked too so a hand-edited file is not skipped silently.
    /// </summary>
    private static IEnumerable<JObject> Workspaces(JObject root)
    {
        yield return root;

        if (root["Profiles"] is not JArray profiles)
            yield break;

        foreach (JObject profile in profiles.OfType<JObject>())
        {
            if (profile["Workspaces"] is not JArray workspaces) continue;

            foreach (JObject workspace in workspaces.OfType<JObject>())
                yield return workspace;
        }
    }

    private static void MigrateButton(JObject button)
    {
        if (button["States"] is not JArray states) return;

        foreach (JObject state in states.OfType<JObject>())
        {
            // Defensive: a partially upgraded file must keep the flag it already has.
            if (state["BackgroundEnabled"] != null) continue;

            state["BackgroundEnabled"] = !IsUntouchedBlack(state["BackColor"]?.ToString());
        }
    }

    /// <summary>True for the default background color — the one a state never had set by hand.</summary>
    private static bool IsUntouchedBlack(string backColor)
    {
        if (string.IsNullOrWhiteSpace(backColor))
            return true;

        try
        {
            Color color = Color.Parse(backColor);
            return color.R == 0 && color.G == 0 && color.B == 0;
        }
        catch (FormatException)
        {
            // Unparseable color: treat it as the default rather than turning a background on.
            return true;
        }
    }
}
