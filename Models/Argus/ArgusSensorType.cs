namespace LoupixDeck.Models.Argus;

// Mirrors ARGUS_MONITOR_SENSOR_TYPE in argus_monitor_data_api.h.
// Values are the ordinal positions in the original C++ enum and must not be reordered.
public enum ArgusSensorType : uint
{
    Invalid = 0,
    Temperature,
    SyntheticTemperature,
    FanSpeedRpm,
    FanControlValue,
    NetworkSpeed,
    CpuTemperature,
    CpuTemperatureAdditional,
    CpuMultiplier,
    CpuFrequencyFsb,
    GpuTemperature,
    GpuName,
    GpuLoad,
    GpuCoreClk,
    GpuMemoryClk,
    GpuShaderClk,
    GpuFanSpeedPercent,
    GpuFanSpeedRpm,
    GpuMemoryUsedPercent,
    GpuMemoryUsedMb,
    GpuPower,
    DiskTemperature,
    DiskTransferRate,
    CpuLoad,
    RamUsage,
    Battery,
    CpuPower,
    Max,

    // Computed (not part of the native Argus API enum). Synthesized by ArgusMonitorService
    // from CpuFrequencyFsb (index 0) and CpuMultiplier[coreIndex]. Kept above Max so it
    // never collides with future native additions.
    CpuFrequency = 1000,
    CpuFrequencyMax = 1001,
    CpuFrequencyMin = 1002,
    CpuFrequencyAvg = 1003,
    CpuMultiplierMax = 1004,
    CpuMultiplierMin = 1005,
    CpuMultiplierAvg = 1006,
}
