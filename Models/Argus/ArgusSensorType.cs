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
    Max
}
