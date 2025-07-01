using System.Text;
using LoupixDeck.Models;
using Newtonsoft.Json.Linq;
using Zeroconf;

namespace LoupixDeck.Services;

public interface IElgatoController
{
    event EventHandler<KeyLight> KeyLightFound;
    event EventHandler<string> KeylightDisconnected;
    Task ProbeForElgatoDevices();
    Task<bool> InitDeviceAsync(KeyLight keyLight);
    Task Toggle(KeyLight keyLight);
    Task SetBrightness(KeyLight keyLight, int brightness);
    Task SetTemperature(KeyLight keyLight, int temperature);
    Task SetHue(KeyLight keyLight, int hue);
    Task SetSaturation(KeyLight keyLight, int saturation);
    void Dispose();
}

public class ElgatoController : IDisposable, IElgatoController
{
    public event EventHandler<KeyLight> KeyLightFound;
    public event EventHandler<string> KeylightDisconnected;

    private ZeroconfResolver.ResolverListener _listener;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public async Task ProbeForElgatoDevices()
    {
        _listener = ZeroconfResolver.CreateListener("_elg._tcp.local.", 4000, 2, TimeSpan.FromMinutes(2));

        _listener.ServiceFound += (s, e) =>
        {
            var keyLight = new KeyLight(e.DisplayName, e.Services.Values.First().Port, e.IPAddress);

            KeyLightFound?.Invoke(s, keyLight);
        };

        _listener.ServiceLost += (s, e) => { KeylightDisconnected?.Invoke(s, e.DisplayName); };
        
        //I'll give those ******* Elgato Keylights 2 Minutes to be found...
        await Task.Delay(TimeSpan.FromMinutes(2));
        
        _listener.Dispose();
    }

    public async Task<bool> InitDeviceAsync(KeyLight keyLight)
    {
        try
        {
            var response = await _httpClient.GetAsync(keyLight.Url);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();

            InitKeyLight(keyLight, JObject.Parse(responseContent));
            
            return true;
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            throw new Exception("Request was canceled or timed out after retries", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception("An error occurred during the web request after retries", ex);
        }
    }

    private void InitKeyLight(KeyLight keyLight, JObject json)
    {
        var light = json["lights"].First();

        keyLight.On = light?["on"] != null && (Int32)light["on"] == 1;

        keyLight.Brightness = light?["brightness"] != null ? (Int32)light["brightness"] : 0;
        keyLight.Temperature = light?["temperature"] != null ? (Int32)light["temperature"] : 0;
        keyLight.Hue = light?["hue"] != null ? (Int32)light["hue"] : 0;
        keyLight.Saturation = light?["saturation"] != null ? (Int32)light["saturation"] : 0;
    }

    public async Task Toggle(KeyLight keyLight)
    {
        await SetState(keyLight, !keyLight.On);
    }

    private async Task SetState(KeyLight keyLight, bool on)
    {
        if (keyLight.On == on)
        {
            return;
        }

        var jsonData = $"{{\"lights\":[{{\"on\":{Convert.ToInt32(on)}}}]}}";

        await SendPutRequestAsync(keyLight.Url, jsonData);

        keyLight.On = on;
    }

    public async Task SetBrightness(KeyLight keyLight, int brightness)
    {
        if (keyLight.Brightness == brightness)
        {
            return;
        }

        var jsonData = $"{{\"lights\":[{{\"brightness\":{brightness}}}]}}";

        await SendPutRequestAsync(keyLight.Url, jsonData);

        keyLight.Brightness = brightness;
    }

    public async Task SetTemperature(KeyLight keyLight, int temperature)
    {
        if (keyLight.Temperature == temperature)
        {
            return;
        }

        var jsonData = $"{{\"lights\":[{{\"temperature\":{temperature}}}]}}";

        await SendPutRequestAsync(keyLight.Url, jsonData);

        keyLight.Temperature = temperature;
    }

    public async Task SetHue(KeyLight keyLight, int hue)
    {
        if (keyLight.Hue == hue)
        {
            return;
        }

        var jsonData = $"{{\"lights\":[{{\"hue\":{hue}}}]}}";

        await SendPutRequestAsync(keyLight.Url, jsonData);

        keyLight.Hue = hue;
    }

    public async Task SetSaturation(KeyLight keyLight, int saturation)
    {
        if (keyLight.Saturation == saturation)
        {
            return;
        }

        var jsonData = $"{{\"lights\":[{{\"saturation\":{saturation}}}]}}";

        await SendPutRequestAsync(keyLight.Url, jsonData);

        keyLight.Saturation = saturation;
    }

    private async Task SendPutRequestAsync(string url, string jsonData)
    {
        var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PutAsync(url, content);

            response.EnsureSuccessStatusCode();

            await response.Content.ReadAsStringAsync();
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}