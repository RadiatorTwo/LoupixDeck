using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models.Converter;

public class SKBitmapBase64Converter : JsonConverter<SKBitmap>
{
    public override void WriteJson(JsonWriter writer, SKBitmap value, JsonSerializer serializer)
    {
        using var image = SKImage.FromBitmap(value);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var base64 = Convert.ToBase64String(data.ToArray());
        writer.WriteValue(base64);
    }

    public override SKBitmap ReadJson(JsonReader reader, Type objectType, SKBitmap existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.String)
            return null;

        var base64 = (string)reader.Value;
        var bytes = Convert.FromBase64String(base64 ?? string.Empty);
        
        return SKBitmap.Decode(bytes);
    }
}