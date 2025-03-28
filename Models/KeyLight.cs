namespace LoupixDeck.Models;

public class KeyLight(string displayName, int port, string address)
{
    public string DisplayName { get; } = displayName;
    private int Port { get; } = port;
    private string Address { get; } = address;
    public string Url => $"http://{Address}:{Port}/elgato/lights";

    public bool On { get; set; }
    public int Brightness { get; set; }
    public int Temperature { get; set; }
    public int Hue { get; set; }
    public int Saturation { get; set; }
}