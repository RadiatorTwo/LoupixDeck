using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Reads and writes a <see cref="CommandBinding"/>.
/// </summary>
/// <remarks>
/// <para>Reads both forms: the legacy string (<c>"a(x) &amp;&amp; b(y)"</c>, every file saved
/// before structured storage) and the array form below.</para>
/// <para>Writes a chain of text calls only as the legacy string, so a file that was loaded and saved
/// without edits keeps its command strings byte for byte, and an empty chain as <c>""</c>. A chain
/// with a structured call is written as an array:</para>
/// <code>
/// [ { "Name": "System.PasteText", "Parameters": ["a, b\n\n"] }, { "Text": "notepad.exe" } ]
/// </code>
/// <para>JSON null stays null.</para>
/// </remarks>
public sealed class CommandBindingJsonConverter : JsonConverter<CommandBinding>
{
    private const string NameKey = "Name";
    private const string ParametersKey = "Parameters";
    private const string TextKey = "Text";

    public override CommandBinding ReadJson(JsonReader reader, Type objectType, CommandBinding existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
            case JsonToken.Undefined:
                return null;
            case JsonToken.String:
                return CommandBinding.FromLegacy((string)reader.Value);
            case JsonToken.StartArray:
                return FromArray(JArray.Load(reader));
            default:
                // A number or bool a hand-edited file put here: read it as legacy text.
                return CommandBinding.FromLegacy(JToken.Load(reader).ToString());
        }
    }

    /// <summary>Reads a chain from a JSON token of either form; null for JSON null.</summary>
    public static CommandBinding FromToken(JToken token) => token?.Type switch
    {
        null or JTokenType.Null or JTokenType.Undefined => null,
        JTokenType.String => CommandBinding.FromLegacy(token.Value<string>()),
        JTokenType.Array => FromArray((JArray)token),
        _ => CommandBinding.FromLegacy(token.ToString())
    };

    /// <summary>The JSON token <see cref="WriteJson"/> writes for <paramref name="chain"/>.</summary>
    public static JToken ToToken(CommandBinding chain)
    {
        if (chain == null)
            return JValue.CreateNull();
        if (chain.IsAllText)
            return new JValue(chain.ToLegacyString());

        JArray array = [];
        foreach (CommandCall call in chain)
        {
            array.Add(call.IsText
                ? new JObject { [TextKey] = call.Text }
                : new JObject { [NameKey] = call.Name, [ParametersKey] = new JArray(call.GetParameters()) });
        }

        return array;
    }

    public override void WriteJson(JsonWriter writer, CommandBinding value, JsonSerializer serializer) =>
        ToToken(value).WriteTo(writer);

    private static CommandBinding FromArray(JArray array)
    {
        List<CommandCall> calls = [];
        foreach (JToken item in array)
        {
            switch (item)
            {
                case JValue { Type: JTokenType.String } text:
                    // A bare string element: legacy text, possibly a chain of its own.
                    calls.AddRange(CommandBinding.FromLegacy(text.Value<string>()));
                    break;
                case JObject obj when obj[TextKey] is { Type: JTokenType.String } textToken:
                    calls.Add(CommandCall.FromText(textToken.Value<string>()));
                    break;
                case JObject obj:
                    calls.Add(CommandCall.Create(obj[NameKey]?.Value<string>(), ReadParameters(obj[ParametersKey])));
                    break;
            }
        }

        return new CommandBinding(calls);
    }

    private static IEnumerable<string> ReadParameters(JToken token) =>
        token is JArray values
            ? values.Select(v => v.Type is JTokenType.Null or JTokenType.Undefined ? string.Empty : v.ToString())
            : [];
}
