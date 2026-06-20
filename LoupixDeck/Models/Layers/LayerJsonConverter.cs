using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// Polymorphic Newtonsoft JSON converter for <see cref="LayerBase"/>. Reads/writes
/// a "kind" discriminator property so the JSON stays stable independent of the
/// CLR type name (no TypeNameHandling required).
/// </summary>
public class LayerJsonConverter : JsonConverter<LayerBase>
{
    private const string KindProperty = "kind";

    public override bool CanWrite => true;
    public override bool CanRead => true;

    public override void WriteJson(JsonWriter writer, LayerBase value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var token = JObject.FromObject(value, JsonSerializer.Create(StripSelf(serializer)));
        token.AddFirst(new JProperty(KindProperty, value.LayerKind));
        token.WriteTo(writer);
    }

    public override LayerBase ReadJson(JsonReader reader, Type objectType, LayerBase existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        var obj = JObject.Load(reader);
        var kind = obj[KindProperty]?.Value<string>();

        LayerBase target = kind switch
        {
            ImageLayer.Kind => new ImageLayer(),
            TextLayer.Kind => new TextLayer(),
            SymbolLayer.Kind => new SymbolLayer(),
            PluginLayer.Kind => new PluginLayer(),
            _ => throw new JsonSerializationException($"Unknown layer kind '{kind}'.")
        };

        using var sub = obj.CreateReader();
        JsonSerializer.Create(StripSelf(serializer)).Populate(sub, target);
        return target;
    }

    /// <summary>
    /// Returns serializer settings without this converter so that
    /// JObject.FromObject / Populate use the default object serializer for the
    /// concrete type instead of recursing into this converter.
    /// </summary>
    private static JsonSerializerSettings StripSelf(JsonSerializer source)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = source.Formatting,
            NullValueHandling = source.NullValueHandling,
            DefaultValueHandling = source.DefaultValueHandling,
            ContractResolver = source.ContractResolver,
            ReferenceLoopHandling = source.ReferenceLoopHandling
        };

        foreach (var converter in source.Converters)
        {
            if (converter is LayerJsonConverter) continue;
            settings.Converters.Add(converter);
        }

        return settings;
    }
}
