using System.Collections.ObjectModel;
using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models.Converter;

public static class VibrationPatternCatalog
{
    public static ObservableCollection<VibrationPatternItem> All { get; } =
    [
        new VibrationPatternItem("Click Strong",    Constants.VibrationPattern.Short),
        new VibrationPatternItem("Click Medium",    Constants.VibrationPattern.StrongClickMed),
        new VibrationPatternItem("Click Soft",      Constants.VibrationPattern.StrongClickSoft),
        new VibrationPatternItem("Sharp Click",     Constants.VibrationPattern.SharpClick),
        new VibrationPatternItem("Soft Bump",       Constants.VibrationPattern.SoftBump),
        new VibrationPatternItem("Double Click",    Constants.VibrationPattern.Medium),
        new VibrationPatternItem("Triple Click",    Constants.VibrationPattern.TripleClick),
        new VibrationPatternItem("Alert 750ms",     Constants.VibrationPattern.Long),
        new VibrationPatternItem("Alert 1000ms",    Constants.VibrationPattern.LongAlert),
        new VibrationPatternItem("Strong Buzz",     Constants.VibrationPattern.StrongBuzz),
        new VibrationPatternItem("Long Buzz",       Constants.VibrationPattern.VeryLong),
        new VibrationPatternItem("Soft Buzz",       Constants.VibrationPattern.ShortLower),
        new VibrationPatternItem("Smooth Hum",      Constants.VibrationPattern.Rumble5),
        new VibrationPatternItem("Ramp Up Smooth",  Constants.VibrationPattern.AscendSlow),
        new VibrationPatternItem("Ramp Up Sharp",   Constants.VibrationPattern.AscendFast),
        new VibrationPatternItem("Ramp Down",       Constants.VibrationPattern.DescendSlow)
    ];
}
