using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v6 → v7: introduces stateful buttons (issue #131). Each button's
/// per-appearance fields move into a single default <c>ButtonState</c>; those fields are now
/// projected from the active state and no longer serialize at the button level. The button's
/// <c>Command</c> stays at the top level (it mirrors the active state's command) and is also copied
/// into the default state. A migrated single-state button behaves identically to before.
///
/// Covers every stateful button type in one step (so the schema version is bumped only once for the
/// whole feature):
/// - <c>TouchButton</c> (each page's <c>TouchButtons</c> and each rotary page's FreeDraw
///   <c>StripCanvas</c>): <c>Layers</c>, <c>BackColor</c>, <c>VibrationEnabled</c>,
///   <c>VibrationPattern</c> → the default state.
/// - <c>SimpleButton</c> (the physical LED buttons): <c>ButtonColor</c> → the default state's
///   <c>LedColor</c>.
/// </summary>
public sealed class ButtonStatesMigrator : IConfigMigration
{
    public int FromVersion => 6;

    public void Apply(JObject root, string configFilePath)
    {
        if (root["TouchButtonPages"] is JArray touchPages)
        {
            foreach (var page in touchPages.OfType<JObject>())
            {
                if (page["TouchButtons"] is JArray buttons)
                {
                    foreach (var button in buttons.OfType<JObject>())
                        MigrateTouchButton(button);
                }
            }
        }

        foreach (var key in new[] { "RotaryButtonPages", "LeftRotaryButtonPages", "RightRotaryButtonPages" })
        {
            if (root[key] is not JArray rotaryPages) continue;
            foreach (var page in rotaryPages.OfType<JObject>())
            {
                if (page["StripCanvas"] is JObject strip)
                    MigrateTouchButton(strip);
            }
        }

        if (root["SimpleButtons"] is JArray simpleButtons)
        {
            foreach (var button in simpleButtons.OfType<JObject>())
                MigrateSimpleButton(button);
        }

        root["Version"] = FromVersion + 1;
    }

    private static void MigrateTouchButton(JObject button)
    {
        // Already migrated (defensive — a partially upgraded file should not double-wrap).
        if (button["States"] is JArray existing && existing.Count > 0)
            return;

        var stateId = Guid.NewGuid();

        var state = new JObject
        {
            ["Id"] = stateId,
            ["Name"] = "Default",
            ["Command"] = button["Command"],
            ["BackColor"] = button["BackColor"],
            ["VibrationEnabled"] = button["VibrationEnabled"] ?? false,
            ["Layers"] = button["Layers"] ?? new JArray(),
            ["Transition"] = new JObject { ["Kind"] = 0 } // StateTransitionKind.Stay
        };

        // Carry the raw vibration pattern only when the old config stored one.
        if (button["VibrationPattern"] != null)
            state["VibrationPattern"] = button["VibrationPattern"];

        // Remove the fields that no longer serialize at the button level.
        button.Remove("BackColor");
        button.Remove("VibrationEnabled");
        button.Remove("VibrationPattern");
        button.Remove("Layers");

        WrapInDefaultState(button, state, stateId);
    }

    private static void MigrateSimpleButton(JObject button)
    {
        if (button["States"] is JArray existing && existing.Count > 0)
            return;

        var stateId = Guid.NewGuid();

        var state = new JObject
        {
            ["Id"] = stateId,
            ["Name"] = "Default",
            ["Command"] = button["Command"],
            ["LedColor"] = button["ButtonColor"],
            ["Transition"] = new JObject { ["Kind"] = 0 } // StateTransitionKind.Stay
        };

        button.Remove("ButtonColor"); // now projected from the active state

        WrapInDefaultState(button, state, stateId);
    }

    private static void WrapInDefaultState(JObject button, JObject state, Guid stateId)
    {
        button["States"] = new JArray(state);
        button["DefaultStateId"] = stateId;
        button["Mode"] = 0;               // ButtonStateMode.Local
        button["ResetOnPageChange"] = false;
        button["ResetOnRestart"] = true;
    }
}
