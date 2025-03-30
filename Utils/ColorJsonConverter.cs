using Avalonia.Media;
using Newtonsoft.Json;

namespace LoupixDeck.Utils;

public class ColorJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Color);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var colorString = reader.Value?.ToString();
        return Color.Parse(colorString ?? string.Empty);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is Color color)
        {
            writer.WriteValue(color.ToString());
        }
        else
        {
            writer.WriteNull();
        }
    }
}