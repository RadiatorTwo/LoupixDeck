using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command(
    "System.ElgKlToggle",
    "Toggle Keylight",
    "Elgato Keylights",
    "({KeyLightName})",
    ["KeyLightName"],
    [typeof(string)])]
public class ElgatoKeylightToggleCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parameter count");
            return;
        }

        var keylightName = parameters[0];
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.Toggle(keyLight);
    }
}

[Command(
    "System.ElgKlTemperature",
    "Set Temperature",
    "Elgato Keylights",
    "({KeyLightName},{Temperature})",
    ["KeyLightName", "Temperature"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightTemperatureCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var brightness = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetBrightness(keyLight, brightness);
    }
}

[Command(
    "System.ElgKlBrightness",
    "Set Brightness",
    "Elgato Keylights",
    "({KeyLightName},{Brightness})",
    ["KeyLightName", "Brightness"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightBrightnessCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var brightness = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetBrightness(keyLight, brightness);
    }
}

[Command(
    "System.ElgKlSaturation",
    "Set Saturation",
    "Elgato Keylights",
    "({KeyLightName},{Saturation})",
    ["KeyLightName", "Saturation"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightSaturationCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var saturation = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetSaturation(keyLight, saturation);
    }
}

[Command(
    "System.ElgKlHue",
    "Set Hue",
    "Elgato Keylights",
    "({KeyLightName},{Hue})",
    ["KeyLightName", "Hue"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightHueCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var hue = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetHue(keyLight, hue);
    }
}

[Command(
    "System.ElgKlChangeBrightness",
    "Change Brightness",
    "Elgato Keylights",
    "({KeyLightName},{Step})",
    ["KeyLightName", "Step"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightChangeBrightnessCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var step = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetBrightness(keyLight, keyLight.Brightness + step);
    }
}

[Command(
    "System.ElgKlChangeTemperature",
    "Change Temperature",
    "Elgato Keylights",
    "({KeyLightName},{Step})",
    ["KeyLightName", "Step"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightChangeTemperatureCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var step = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetTemperature(keyLight, keyLight.Temperature + step);
    }
}

[Command(
    "System.ElgKlChangeSaturation",
    "Change Saturation",
    "Elgato Keylights",
    "({KeyLightName},{Step})",
    ["KeyLightName", "Step"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightChangeSaturationCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var step = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetSaturation(keyLight, keyLight.Saturation + step);
    }
}

[Command(
    "System.ElgKlChangeHue",
    "Change Hue",
    "Elgato Keylights",
    "({KeyLightName},{Step})",
    ["KeyLightName", "Step"],
    [typeof(string), typeof(int)])]
public class ElgatoKeylightChangeHueCommand(IElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0];
        var step = Convert.ToInt32(parameters[1]);

        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetHue(keyLight, keyLight.Saturation + step);
    }
}