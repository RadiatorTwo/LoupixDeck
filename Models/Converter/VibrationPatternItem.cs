namespace LoupixDeck.Models.Converter;

public class VibrationPatternItem
{
    public string Name { get; set; }
    public byte Value { get; set; }

    public VibrationPatternItem(string name, byte value)
    {
        Name = name;
        Value = value;
    }

    public override string ToString() => Name;
}
