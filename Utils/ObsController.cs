using LoupixDeck.Models;
using OBSWebsocketDotNet;

namespace LoupixDeck.Utils;

public class ObsController
{
    private readonly OBSWebsocket _obs = new();
    private ObsConfig _obsConfig;

    // Verbindet sich mit OBS
    public void Connect()
    {
        try
        {
            _obsConfig = ObsConfig.LoadConfig();
            _obs.ConnectAsync(_obsConfig.Url, _obsConfig.Password);
            Console.WriteLine("Connected to OBS WebSocket.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to OBS: {ex.Message}");
        }
    }

    // Trennt die Verbindung zu OBS
    public void Disconnect()
    {
        if (_obs == null || !_obs.IsConnected) return;
        
        _obs.Disconnect();
        Console.WriteLine("Disconnected from OBS WebSocket.");
    }

    // Switching Virtual Camera
    public void ToggleVirtualCamera()
    {
        if (!_obs.IsConnected)
        {
            Console.WriteLine("Not connected to OBS.");
            return;
        }

        try
        {
            // Hinweis: Der Name des Requests kann je nach OBS-WebSocket-Version variieren.
            _obs.SendRequest("ToggleVirtualCam", null);
            Console.WriteLine("Toggled Virtual Camera.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling Virtual Camera: {ex.Message}");
        }
    }

    // Zusätzliche Funktion: Streaming starten
    public void StartStreaming()
    {
        if (!_obs.IsConnected)
        {
            Console.WriteLine("Not connected to OBS.");
            return;
        }

        try
        {
            _obs.SendRequest("StartStreaming", null);
            Console.WriteLine("Streaming started.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting stream: {ex.Message}");
        }
    }

    // Zusätzliche Funktion: Streaming stoppen
    public void StopStreaming()
    {
        if (!_obs.IsConnected)
        {
            Console.WriteLine("Not connected to OBS.");
            return;
        }

        try
        {
            _obs.SendRequest("StopStreaming", null);
            Console.WriteLine("Streaming stopped.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping stream: {ex.Message}");
        }
    }
}