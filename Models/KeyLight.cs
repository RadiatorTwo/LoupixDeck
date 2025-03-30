using Newtonsoft.Json;

namespace LoupixDeck.Models;

public class KeyLight
{
    public string DisplayName { get; set; }
    public int Port { get; set; }
    public  string Address { get; set; }

    public KeyLight()
    {
    }
    
    public KeyLight(string displayName, int port, string address)
    {
        DisplayName = displayName;
        Port = port;
        Address = address;
    }

    [JsonIgnore]
    public string Url => $"http://{Address}:{Port}/elgato/lights";

    public bool On { get; set; }
    public int Brightness { get; set; }
    public int Temperature { get; set; }
    public int Hue { get; set; }
    public int Saturation { get; set; }
}