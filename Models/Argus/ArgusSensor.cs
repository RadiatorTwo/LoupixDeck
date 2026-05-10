namespace LoupixDeck.Models.Argus;

public sealed record ArgusSensor(
    ArgusSensorType Type,
    string Label,
    string Unit,
    double Value,
    uint DataIndex,
    uint SensorIndex);
