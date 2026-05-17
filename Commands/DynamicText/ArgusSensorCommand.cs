using LoupixDeck.Commands.Base;
using LoupixDeck.Models.Argus;
using LoupixDeck.Services.Argus;

namespace LoupixDeck.Commands.DynamicText;

[Command("Argus.Sensor", "Argus Sensor", "Argus Monitor",
    parameterTemplate: "({Sensor})",
    parameterNames: ["Sensor"],
    parameterTypes: [typeof(string)],
    Platform = CommandPlatform.Windows)]
public class ArgusSensorCommand : IDynamicTextProvider
{
    private readonly IArgusMonitorService _argus;

    public ArgusSensorCommand(IArgusMonitorService argus)
    {
        _argus = argus;
    }

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public string GetText(string[] parameters)
    {
        if (!_argus.IsAvailable)
            return "N/A";

        if (parameters is not { Length: >= 1 } || string.IsNullOrWhiteSpace(parameters[0]))
            return "?";

        if (!TryParseSensorRef(parameters[0], out var type, out var sensorIndex))
            return "?";

        var sensor = _argus.Sensors.FirstOrDefault(s => s.Type == type && s.SensorIndex == sensorIndex);
        if (sensor is null)
            return "?";

        var unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        return $"{sensor.Value:F1}{unit}";
    }

    private static bool TryParseSensorRef(string raw, out ArgusSensorType type, out uint sensorIndex)
    {
        type = ArgusSensorType.Invalid;
        sensorIndex = 0;

        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
            return false;

        if (!Enum.TryParse(parts[0], ignoreCase: true, out type) || type == ArgusSensorType.Invalid)
            return false;

        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
            return true;

        return uint.TryParse(parts[1], out sensorIndex);
    }

    public Task Execute(string[] parameters) => Task.CompletedTask;
}
