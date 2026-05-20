namespace LoupixDeck.Models.HwInfo;

// Mirrors SENSOR_READING_TYPE in HWiNFO's shared-memory SDK (HWiNFO Shared Memory Support).
// Values are the ordinal positions in the original C++ enum and must not be reordered.
public enum HwInfoReadingType : uint
{
    None = 0,
    Temperature,
    Voltage,
    Fan,
    Current,
    Power,
    Clock,
    Usage,
    Other,
}
