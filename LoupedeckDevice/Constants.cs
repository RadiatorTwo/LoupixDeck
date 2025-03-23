using LoupixDeck.Utils;

namespace LoupixDeck.LoupedeckDevice;

public static class Constants
{
    public enum ButtonType
    {
        KNOB_TL = 0,
        KNOB_CL = 1,
        KNOB_BL = 2,
        KNOB_TR = 3,
        KNOB_CR = 4,
        KNOB_BR = 5,
        BUTTON0 = 6,
        BUTTON1 = 7,
        BUTTON2 = 8,
        BUTTON3 = 9,
        BUTTON4 = 10,
        BUTTON5 = 11,
        BUTTON6 = 12,
        BUTTON7 = 13
    }

    public static readonly Dictionary<byte, ButtonType> Buttons = new()
    {
        { 0x01, ButtonType.KNOB_TL },
        { 0x02, ButtonType.KNOB_CL },
        { 0x03, ButtonType.KNOB_BL },
        { 0x04, ButtonType.KNOB_TR },
        { 0x05, ButtonType.KNOB_CR },
        { 0x06, ButtonType.KNOB_BR },
        { 0x07, ButtonType.BUTTON0 },
        { 0x08, ButtonType.BUTTON1 },
        { 0x09, ButtonType.BUTTON2 },
        { 0x0a, ButtonType.BUTTON3 },
        { 0x0b, ButtonType.BUTTON4 },
        { 0x0c, ButtonType.BUTTON5 },
        { 0x0d, ButtonType.BUTTON6 },
        { 0x0e, ButtonType.BUTTON7 }
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

    public enum ButtonEventType
    {
        BUTTON_DOWN = 0,
        BUTTON_UP = 1
    }

    public enum TouchEventType
    {
        TOUCH_START = 0,
        TOUCH_END = 1,
        TOUCH_MOVE = 2
    }

    public enum SystemCommand
    {
        NONE,
        NEXT_PAGE,
        PREVIOUS_PAGE,
        NEXT_ROT_PAGE,
        PREVIOUS_ROT_PAGE,
        OBS_START_RECORD,
        OBS_STOP_RECORD,
        OBS_PAUSE_RECORD,
        OBS_VIRTUAL_CAM
    }

    public static readonly BiMap<string, SystemCommand> SystemCommands = new()
    {
        { "System.NextPage", SystemCommand.NEXT_PAGE },
        { "System.PreviousPage", SystemCommand.PREVIOUS_PAGE },
        { "System.NextRotaryPage", SystemCommand.NEXT_ROT_PAGE },
        { "System.PreviousRotaryPage", SystemCommand.PREVIOUS_ROT_PAGE },
        { "System.ObsStartRecord", SystemCommand.OBS_START_RECORD },
        { "System.ObsStopRecord", SystemCommand.OBS_STOP_RECORD },
        { "System.ObsPauseRecord", SystemCommand.OBS_PAUSE_RECORD },
        { "System.ObsVirtualCam", SystemCommand.OBS_VIRTUAL_CAM }
    };
}