using LoupixDeck.Models;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>
/// Home Assistant through the Home Assistant plugin: lights, climate, scenes and a sensor. The
/// entity ids are examples the user replaces with their own.
/// </summary>
internal sealed class HomeAssistantTemplate : StarterProfileTemplate
{
    private static readonly StarterPluginRequirement HomeAssistant = new("homeassistant", "Home Assistant");

    public override string Id => "home-assistant";
    protected override string NameKey => "StarterProfile_HomeAssistant_Name";
    protected override string DescriptionKey => "StarterProfile_HomeAssistant_Description";
    protected override string SetupNoteKey => "StarterProfile_HomeAssistant_SetupNote";
    public override string SymbolId => "home-automation";

    public override IReadOnlyList<StarterPluginRequirement> GetRequiredPlugins(bool isWindows) => [HomeAssistant];

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace workspace = b.AddWorkspace(profile, "Home");
        TouchButtonPage page = b.AddTouchPage(workspace, "Rooms");

        // The entity commands draw the entity's icon, name and state themselves.
        b.Display(page, 0, 0, "HomeAssistant.ToggleEntity(light.living_room)");
        b.Display(page, 0, 1, "HomeAssistant.ToggleEntity(light.kitchen)");
        b.Display(page, 0, 2, "HomeAssistant.ToggleEntity(light.bedroom)");
        b.Display(page, 0, 3, "HomeAssistant.ToggleEntity(switch.coffee_machine)");
        b.Display(page, 0, 4, "HomeAssistant.ConnectionStatus");

        b.Display(page, 1, 0, "HomeAssistant.ShowEntity(sensor.outdoor_temperature)");
        b.Display(page, 1, 1, "HomeAssistant.DecreaseTemperature(climate.living_room)");
        b.Display(page, 1, 2, "HomeAssistant.ShowEntity(climate.living_room)");
        b.Display(page, 1, 3, "HomeAssistant.IncreaseTemperature(climate.living_room)");
        b.Display(page, 1, 4, "HomeAssistant.OpenEntityControls(light.living_room)");

        b.Display(page, 2, 0, "HomeAssistant.ActivateScene(scene.movie_night)");
        b.Display(page, 2, 1, "HomeAssistant.ActivateScene(scene.good_night)");
        b.Display(page, 2, 2, "HomeAssistant.RunScript(script.leave_home)");

        // A brightness dial dims on a turn and toggles the light on a press.
        b.AddDialPage(workspace, "Lights",
            Brightness("Living room", "light.living_room"),
            Brightness("Kitchen", "light.kitchen"),
            Brightness("Bedroom", "light.bedroom"));
    }

    private static StarterDial Brightness(string label, string entityId)
    {
        string command = $"HomeAssistant.AdjustBrightness({entityId})";
        return new StarterDial(label, command, command, command);
    }
}