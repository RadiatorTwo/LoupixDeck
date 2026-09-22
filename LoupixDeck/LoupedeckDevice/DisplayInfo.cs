namespace LoupixDeck.LoupedeckDevice;

public class DisplayInfo
{
    public required byte[] Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// True when this framebuffer expects RGB565 pixels most-significant byte first. Most
    /// Loupedeck displays are little-endian; the Loupedeck CT's wheel screen is not, and
    /// sending it little-endian data swaps red and blue (verified on hardware: a frame forced
    /// to 0xF800 red renders blue).
    /// </summary>
    public bool BigEndianPixels { get; set; }
}