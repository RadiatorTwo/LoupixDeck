using Newtonsoft.Json;
using SkiaSharp;
using System;

public class SKBitmapBase64Converter : JsonConverter<SKBitmap>
{
    public override void WriteJson(JsonWriter writer, SKBitmap value, JsonSerializer serializer)
    {
        using var image = SKImage.FromBitmap(value);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        string base64 = Convert.ToBase64String(data.ToArray());
        writer.WriteValue(base64);
    }

    public override SKBitmap ReadJson(JsonReader reader, Type objectType, SKBitmap existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.String)
            return null;

        string base64 = (string)reader.Value;
        byte[] bytes = Convert.FromBase64String(base64);
        return SKBitmap.Decode(bytes);
    }
}