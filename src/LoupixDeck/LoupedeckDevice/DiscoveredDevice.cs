namespace LoupixDeck.LoupedeckDevice;

public class DiscoveredDevice
{
    public Type ConnectionType { get; set; }
    public string Path { get; set; }
    public string VendorId { get; set; }
    public string ProductId { get; set; }
    public string SerialNumber { get; set; }
    public string Host { get; set; }
}