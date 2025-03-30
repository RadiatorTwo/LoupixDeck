using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command("System.ElgKlToggle")]
public class ElgatoKeylightToggleCommand(ElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0] as string;
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.Toggle(keyLight);
    }
}

[Command("System.ElgKlTemperature")]
public class ElgatoKeylightTemperatureCommand(ElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0] as string;
        var brightness = Convert.ToInt32(parameters[1] as string);
        
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetBrightness(keyLight, brightness);
    }
}

[Command("System.ElgKlBrightness")]
public class ElgatoKeylightBrightnessCommand(ElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0] as string;
        var brightness = Convert.ToInt32(parameters[1] as string);
        
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetBrightness(keyLight, brightness);
    }
}

[Command("System.ElgKlSaturation")]
public class ElgatoKeylightSaturationCommand(ElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0] as string;
        var saturation = Convert.ToInt32(parameters[1] as string);
        
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetSaturation(keyLight, saturation);
    }
}

[Command("System.ElgKlHue")]
public class ElgatoKeylightHueCommand(ElgatoController elgato, ElgatoDevices elgatoDevices) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 2)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        var keylightName = parameters[0] as string;
        var hue = Convert.ToInt32(parameters[1] as string);
        
        var keyLight = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == keylightName);

        if (keyLight == null) return;

        await elgato.SetHue(keyLight, hue);
    }
}