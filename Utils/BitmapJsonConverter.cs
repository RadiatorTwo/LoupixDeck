using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace LoupixDeck.Utils;

public class BitmapJsonConverter : JsonConverter<Bitmap>
{
    public override Bitmap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string base64String = reader.GetString();
        if (string.IsNullOrEmpty(base64String))
            return null;

        byte[] bytes = Convert.FromBase64String(base64String);
        using MemoryStream ms = new(bytes);
        return new Bitmap(ms);
    }

    public override void Write(Utf8JsonWriter writer, Bitmap value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        using MemoryStream ms = new();
        value.Save(ms);
        string base64String = Convert.ToBase64String(ms.ToArray());
        writer.WriteStringValue(base64String);
    }
}