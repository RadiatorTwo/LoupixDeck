namespace LoupixDeck.Models.Converter;

public class VibrationPatternItem(string name, byte value)
{
    public string Name { get; set; } = name;
    public byte Value { get; set; } = value;

    public override string ToString() => Name;
}
