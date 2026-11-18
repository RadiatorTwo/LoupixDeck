using LoupixDeck.Localization;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// One choice of an enum command parameter: <see cref="Value"/> is what the command string
/// stores, <see cref="Label"/> what the dropdown shows.
/// </summary>
public sealed record ParameterOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The dropdown choices of an enum command parameter on this device. A parameter that names a
/// button offers only the buttons the device has, labelled the way the device prints them.
/// </summary>
public interface ICommandParameterOptions
{
    IReadOnlyList<ParameterOption> For(Type enumType);
}

public sealed class CommandParameterOptions(IDeviceService deviceService) : ICommandParameterOptions
{
    public IReadOnlyList<ParameterOption> For(Type enumType)
    {
        if (enumType == typeof(LedButton))
            return LedButtons();
        if (enumType == typeof(TouchButtonNumber))
            return TouchButtons();

        return Enum.GetNames(enumType).Select(n => new ParameterOption(n, n)).ToList();
    }

    private List<ParameterOption> LedButtons()
    {
        LoupedeckDevice.Device.LoupedeckDevice device = deviceService.Device;
        // Without a device every button is offered, so a command still opens with its choice.
        HashSet<Constants.ButtonType> present = device?.Buttons == null
            ? null
            : device.Buttons.Select(i => Constants.ButtonType.BUTTON0 + i).ToHashSet();
        // The CT prints 1-8 on its round buttons, the other devices 0-7.
        int firstNumber = device is LoupedeckDevice.Device.LoupedeckCtDevice ? 1 : 0;

        List<ParameterOption> options = [];
        foreach (LedButton led in Enum.GetValues<LedButton>())
        {
            if (led == LedButton.All) continue;
            Constants.ButtonType type = (Constants.ButtonType)led;
            if (present != null && !present.Contains(type)) continue;

            string label = type <= Constants.ButtonType.BUTTON7
                ? Loc.Tr("CommandOption_Button", (int)(type - Constants.ButtonType.BUTTON0) + firstNumber)
                : NamedKeyLabel(led);
            options.Add(new ParameterOption(led.ToString(), label));
        }

        options.Add(new ParameterOption(nameof(LedButton.All), Loc.Tr("CommandOption_All")));
        return options;
    }

    private List<ParameterOption> TouchButtons()
    {
        int count = deviceService.TouchButtonCount;
        List<ParameterOption> options = [];
        foreach (TouchButtonNumber number in Enum.GetValues<TouchButtonNumber>())
        {
            if (number == TouchButtonNumber.All) continue;
            if (count > 0 && (int)number > count) continue;
            options.Add(new ParameterOption(number.ToString(), Loc.Tr("CommandOption_Button", (int)number)));
        }

        options.Add(new ParameterOption(nameof(TouchButtonNumber.All), Loc.Tr("CommandOption_All")));
        return options;
    }

    /// <summary>The Loupedeck CT's square keys, named as the virtual device names them.</summary>
    private static string NamedKeyLabel(LedButton led) => led switch
    {
        LedButton.Home => Loc.Tr("VirtualDevice_ButtonHome"),
        LedButton.Undo => Loc.Tr("VirtualDevice_ButtonUndo"),
        LedButton.Keyboard => Loc.Tr("VirtualDevice_ButtonKeyboard"),
        LedButton.Enter => Loc.Tr("VirtualDevice_ButtonEnter"),
        LedButton.Save => Loc.Tr("VirtualDevice_ButtonSave"),
        LedButton.FnLeft => Loc.Tr("CommandOption_FnLeft"),
        LedButton.FnRight => Loc.Tr("CommandOption_FnRight"),
        _ => led.ToString()
    };
}
