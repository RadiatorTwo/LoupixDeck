namespace LoupixDeck.LoupedeckDevice;

public static class Constants
{
    public static readonly Dictionary<byte, string> Buttons = new()
    {
        { 0x01, "knobTL" },
        { 0x02, "knobCL" },
        { 0x03, "knobBL" },
        { 0x04, "knobTR" },
        { 0x05, "knobCR" },
        { 0x06, "knobBR" },
        { 0x07, "0" },
        { 0x08, "1" },
        { 0x09, "2" },
        { 0x0a, "3" },
        { 0x0b, "4" },
        { 0x0c, "5" },
        { 0x0d, "6" },
        { 0x0e, "7" }
    };

    public const int ConnectionTimeout = 3000;
    public const int DefaultReconnectInterval = 3000;
    public const int MaxBrightness = 10;

    public enum Command : byte
    {
        BUTTON_PRESS = 0x00,
        KNOB_ROTATE = 0x01,
        SET_COLOR = 0x02,
        SERIAL = 0x03,
        RESET = 0x06,
        VERSION = 0x07,
        SET_BRIGHTNESS = 0x09,
        FRAMEBUFF = 0x10,
        SET_VIBRATION = 0x1b,
        MCU = 0x0d,
        DRAW = 0x0f,
        TOUCH = 0x4d,
        TOUCH_END = 0x6d
    }

    public static class VibrationPattern
    {
        public const byte Short = 0x01;
        public const byte Medium = 0x0a;
        public const byte Long = 0x0f;
        public const byte Low = 0x31;
        public const byte ShortLow = 0x32;
        public const byte ShortLower = 0x33;
        public const byte Lower = 0x40;
        public const byte Lowest = 0x41;
        public const byte DescendSlow = 0x46;
        public const byte DescendMed = 0x47;
        public const byte DescendFast = 0x48;
        public const byte AscendSlow = 0x52;
        public const byte AscendMed = 0x53;
        public const byte AscendFast = 0x58;
        public const byte RevSlowest = 0x5e;
        public const byte RevSlow = 0x5f;
        public const byte RevMed = 0x60;
        public const byte RevFast = 0x61;
        public const byte RevFaster = 0x62;
        public const byte RevFastest = 0x63;
        public const byte RiseFall = 0x6a;
        public const byte Buzz = 0x70;
        public const byte Rumble5 = 0x77;
        public const byte Rumble4 = 0x78;
        public const byte Rumble3 = 0x79;
        public const byte Rumble2 = 0x7a;
        public const byte Rumble1 = 0x7b;
        public const byte VeryLong = 0x76;
    }
}