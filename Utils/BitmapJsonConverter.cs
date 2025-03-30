using Avalonia.Media.Imaging;
using Newtonsoft.Json;

namespace LoupixDeck.Utils;

public class BitmapJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(Bitmap).IsAssignableFrom(objectType);
    }
    
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        
        var base64String = reader.Value?.ToString();
        if (string.IsNullOrEmpty(base64String))
            return null;
        
        var bytes = Convert.FromBase64String(base64String);
        using var ms = new MemoryStream(bytes);
        
        return new Bitmap(ms);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is not Bitmap bitmap) 
        {
            writer.WriteNull();
            return;
        }

        using var ms = new MemoryStream();
        
        bitmap.Save(ms);
        var base64String = Convert.ToBase64String(ms.ToArray());
        writer.WriteValue(base64String);
    }
}