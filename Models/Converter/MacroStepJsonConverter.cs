using LoupixDeck.Models.Macros;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Serializes <see cref="MacroStep"/> instances with a "StepType" discriminator
/// (the <see cref="MacroStepType"/> enum name) instead of CLR type names, so the
/// file format survives refactorings. Steps with an unknown discriminator (written
/// by a newer app version) deserialize to null and are filtered out by the loader.
/// </summary>
public class MacroStepJsonConverter : JsonConverter
{
    private const string DiscriminatorProperty = "StepType";

    // Plain serializer without this converter — used for the inner (per-property)
    // round-trip so WriteJson/ReadJson don't recurse into themselves.
    private static readonly JsonSerializer PlainSerializer = CreatePlainSerializer();

    private static JsonSerializer CreatePlainSerializer()
    {
        var serializer = new JsonSerializer();
        // Enum properties (mouse action/button) as names, not numbers — readable and
        // safe against enum reordering.
        serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        return serializer;
    }

    public override bool CanConvert(Type objectType) => typeof(MacroStep).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is not MacroStep step)
        {
            writer.WriteNull();
            return;
        }

        var obj = JObject.FromObject(step, PlainSerializer);
        obj.AddFirst(new JProperty(DiscriminatorProperty, step.StepType.ToString()));
        obj.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var obj = JObject.Load(reader);
        var discriminator = obj[DiscriminatorProperty]?.Value<string>();

        if (!Enum.TryParse<MacroStepType>(discriminator, ignoreCase: true, out var stepType))
        {
            Console.WriteLine($"[MacroStepJsonConverter] Unknown step type '{discriminator}' — skipping step.");
            return null;
        }

        MacroStep step = stepType switch
        {
            MacroStepType.Text => new TextStep(),
            MacroStepType.KeyCombination => new KeyCombinationStep(),
            MacroStepType.Delay => new DelayStep(),
            MacroStepType.KeyDown => new KeyDownStep(),
            MacroStepType.KeyUp => new KeyUpStep(),
            MacroStepType.Mouse => new MouseStep(),
            MacroStepType.Command => new CommandStep(),
            MacroStepType.RepeatStart => new RepeatStartStep(),
            MacroStepType.RepeatEnd => new RepeatEndStep(),
            MacroStepType.SetVariable => new SetVariableStep(),
            MacroStepType.If => new IfStep(),
            MacroStepType.Else => new ElseStep(),
            MacroStepType.EndIf => new EndIfStep(),
            _ => null
        };

        if (step == null)
            return null;

        using var subReader = obj.CreateReader();
        PlainSerializer.Populate(subReader, step);
        return step;
    }
}
