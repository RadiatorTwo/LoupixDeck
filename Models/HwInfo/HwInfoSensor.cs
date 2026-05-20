namespace LoupixDeck.Models.HwInfo;

/// <summary>
/// A single HWiNFO reading, flattened together with its parent sensor group.
/// <para>
/// HWiNFO identifies a reading stably across runs by the triple
/// (<see cref="SensorId"/>, <see cref="SensorInstance"/>, <see cref="ReadingId"/>) —
/// the section indices themselves are not stable. The HWiNFO.Sensor command
/// references a reading by exactly that triple.
/// </para>
/// </summary>
public sealed record HwInfoSensor(
    HwInfoReadingType Type,
    string SensorName,
    string Label,
    string Unit,
    double Value,
    double ValueMin,
    double ValueMax,
    double ValueAvg,
    uint SensorId,
    uint SensorInstance,
    uint ReadingId);
