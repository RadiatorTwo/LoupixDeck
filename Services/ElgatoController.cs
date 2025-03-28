using LoupixDeck.Models;
using Zeroconf;

namespace LoupixDeck.Services;

public class ElgatoController : IDisposable
{
    public event EventHandler<KeyLight> KeyLightFound;
    public event EventHandler<KeyLight> KeylightDisconnected;

    public readonly Dictionary<string, KeyLight> KeyLights = new();

    private ZeroconfResolver.ResolverListener _listener;

    public void ProbeForElgatoDevices()
    {
        _listener = ZeroconfResolver.CreateListener("_elg._tcp.local.");

        _listener.ServiceFound += (s, e) =>
        {
            var lightInstance = new KeyLight(KeyLights.Count,
                e.DisplayName,
                e.Services.Values.First().Port,
                e.IPAddress);

            lightInstance.InitDeviceAsync().GetAwaiter().GetResult();

            if (KeyLights.ContainsKey(e.DisplayName))
            {
                KeyLights.Remove(e.DisplayName);
            }

            KeyLights.Add(e.DisplayName, lightInstance);
            KeyLightFound?.Invoke(s, lightInstance);
        };

        _listener.ServiceLost += (s, e) =>
        {
            var light = GetKeyLight(e.DisplayName);
            KeyLights.Remove(e.DisplayName);
            KeylightDisconnected?.Invoke(s, light);
        };
    }

    public KeyLight GetKeyLight(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !KeyLights.TryGetValue(name, out var value))
        {
            return null;
        }

        return value;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}