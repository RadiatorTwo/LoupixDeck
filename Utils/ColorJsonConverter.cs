using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace LoupixDeck.Utils;

public class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Color.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}