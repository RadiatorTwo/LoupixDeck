using System.Globalization;
using LoupixDeck.Commands.Base;
using LoupixDeck.Services.HwInfo;

namespace LoupixDeck.Commands.DynamicText;

[Command("HwInfo.Sensor", "HWiNFO Sensor", "HWiNFO",
    parameterTemplate: "({Sensor})",
    parameterNames: ["Sensor"],
    parameterTypes: [typeof(string)],
    Platform = CommandPlatform.Windows)]
public class HwInfoSensorCommand : IDynamicTextProvider
{
    private readonly IHwInfoService _hwInfo;

    public HwInfoSensorCommand(IHwInfoService hwInfo)
    {
        _hwInfo = hwInfo;
    }

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public string GetText(string[] parameters)
    {
        if (!_hwInfo.IsAvailable)
            return "N/A";

        if (parameters is not { Length: >= 1 } || string.IsNullOrWhiteSpace(parameters[0]))
            return "?";

        if (!TryParseSensorRef(parameters[0], out var sensorId, out var sensorInstance, out var readingId))
            return "?";

        var sensor = _hwInfo.Sensors.FirstOrDefault(s =>
            s.SensorId == sensorId && s.SensorInstance == sensorInstance && s.ReadingId == readingId);
        if (sensor is null)
            return "?";

        var unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        return $"{sensor.Value:F1}{unit}";
    }

    // Reference format: "sensorId:sensorInstance:readingId" — the triple HWiNFO keeps
    // stable across runs. Values may be decimal or 0x-prefixed hex.
    private static bool TryParseSensorRef(string raw, out uint sensorId, out uint sensorInstance, out uint readingId)
    {
        sensorId = 0;
        sensorInstance = 0;
        readingId = 0;

        var parts = raw.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        return TryParseUInt(parts[0], out sensorId)
               && TryParseUInt(parts[1], out sensorInstance)
               && TryParseUInt(parts[2], out readingId);
    }

    private static bool TryParseUInt(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, out value);
    }

    public Task Execute(string[] parameters) => Task.CompletedTask;
}
