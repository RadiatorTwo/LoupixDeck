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
        BUTTON7 = 13,

        // Loupedeck CT only. The 12 named square buttons plus KNOB_CT (the centre
        // wheel's rotate identity) — confirmed against real hardware via a serial
        // trace (LOUPIXDECK_DEBUG_PROTOCOL=1). The wheel has no separate "click"
        // button code: pressing it shows up as a tight cluster of touch
        // start/end events near the centre of its own screen (command bytes
        // 0x52/0x72 — see Command.WHEEL_TOUCH below), not as a BUTTON_PRESS.
        // Detecting a "click" from that touch cluster is not yet implemented.
        CT_HOME = 14,
        CT_UNDO = 15,
        CT_KEYBOARD = 16,
        CT_ENTER = 17,
        CT_SAVE = 18,
        CT_FN_L = 19,
        CT_A = 20,
        CT_B = 21,
        CT_C = 22,
        CT_D = 23,
        CT_FN_R = 24,
        CT_E = 25,
        KNOB_CT = 26
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
        { 0x0e, ButtonType.BUTTON7 },

        // Confirmed via serial trace on real hardware (2026-06-18).
        { 0x0f, ButtonType.CT_HOME },
        { 0x10, ButtonType.CT_UNDO },
        { 0x11, ButtonType.CT_KEYBOARD },
        { 0x12, ButtonType.CT_ENTER },
        { 0x13, ButtonType.CT_SAVE },
        { 0x14, ButtonType.CT_FN_L },
        { 0x15, ButtonType.CT_A },
        { 0x16, ButtonType.CT_C },
        { 0x17, ButtonType.CT_FN_R },
        { 0x18, ButtonType.CT_B },
        { 0x19, ButtonType.CT_D },
        { 0x1a, ButtonType.CT_E },
        // The wheel's KNOB_ROTATE frames report button-byte 0x00 (confirmed via
        // trace) — not 0x1b as originally guessed from the community drivers.
        { 0x00, ButtonType.KNOB_CT }
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
        SET_HAPTIC_STRENGTH = 0x19,
        SET_HAPTIC = 0x2e,
        MCU = 0x0d,
        DRAW = 0x0f,
        TOUCH = 0x4d,
        TOUCH_END = 0x6d,

        // Loupedeck CT only: the centre wheel's own 240x240 touchscreen reports
        // touch start/move and end on these bytes instead of TOUCH/TOUCH_END —
        // confirmed via serial trace on real hardware (2026-06-18). Payload format
        // is identical to TOUCH/TOUCH_END (x/y/touchId).
        WHEEL_TOUCH = 0x52,
        WHEEL_TOUCH_END = 0x72
    }

    // Vibration patterns map directly to effect IDs in the DRV2605 haptic chip
    // (see TI datasheet, table 11.2). Names below are the historical
    // Loupedeck-community labels (kept for compatibility); the inline comments
    // show what each byte actually triggers per the official DRV2605 spec.
    public static class VibrationPattern
    {
        public const byte Off              = 0x00; // stop / no effect
        public const byte Short            = 0x01; // Strong Click 100%
        public const byte StrongClickMed   = 0x02; // Strong Click 60%
        public const byte StrongClickSoft  = 0x03; // Strong Click 30%
        public const byte SharpClick       = 0x04; // Sharp Click 100%
        public const byte SoftBump         = 0x07; // Soft Bump 100%
        public const byte Medium           = 0x0a; // Double Click 100% (not a medium click)
        public const byte TripleClick      = 0x0c; // Triple Click 100%
        public const byte StrongBuzz       = 0x0e; // Strong Buzz 100% (short)
        public const byte Long             = 0x0f; // 750ms Alert (not a long click)
        public const byte LongAlert        = 0x10; // 1000ms Alert
        public const byte Low         = 0x31; // Buzz 3 (60%)
        public const byte ShortLow    = 0x32; // Buzz 4 (40%)
        public const byte ShortLower  = 0x33; // Buzz 5 (20%) — default fallback
        public const byte Lower       = 0x40; // Transition Hum 1 (100%)
        public const byte Lowest      = 0x41; // Transition Hum 2 (80%)
        public const byte DescendSlow = 0x46; // Ramp Down Long Smooth 100→0%
        public const byte DescendMed  = 0x47; // Ramp Down Long Smooth (variant)
        public const byte DescendFast = 0x48; // Ramp Down Medium Smooth 100→0%
        public const byte AscendSlow  = 0x52; // Ramp Up Long Smooth 0→100%
        public const byte AscendMed   = 0x53; // Ramp Up Long Smooth (variant)
        public const byte AscendFast  = 0x58; // Ramp Up Long Sharp 0→100%
        public const byte RevSlowest  = 0x5e; // Ramp Down Long Smooth 50→0%
        public const byte RevSlow     = 0x5f; // Ramp Down Long Smooth 50→0% (variant)
        public const byte RevMed      = 0x60; // Ramp Down Medium Smooth 50→0%
        public const byte RevFast     = 0x61; // Ramp Down Medium Smooth 50→0% (variant)
        public const byte RevFaster   = 0x62; // Ramp Down Short Smooth 50→0%
        public const byte RevFastest  = 0x63; // Ramp Down Short Smooth 50→0% (variant)
        public const byte RiseFall    = 0x6a; // Ramp Up Long Smooth 0→50% (NOT rise+fall — just small ascent)
        public const byte Buzz        = 0x70; // Ramp Up Long Sharp 0→50% (NOT a buzz! Misnomer)
        public const byte VeryLong    = 0x76; // Long Buzz for programmatic stopping — THE real continuous buzz
        public const byte Rumble5     = 0x77; // Smooth Hum 1 (50%) — strongest hum
        public const byte Rumble4     = 0x78; // Smooth Hum 2 (40%)
        public const byte Rumble3     = 0x79; // Smooth Hum 3 (30%)
        public const byte Rumble2     = 0x7a; // Smooth Hum 4 (20%)
        public const byte Rumble1     = 0x7b; // Smooth Hum 5 (10%) — weakest hum
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
}