using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Null-tolerant accessors for the loosely shaped Loupedeck JSON. Every field of a <c>.lp5</c> file is
/// optional in practice, so lookups return null / empty instead of throwing on a missing or mistyped
/// value.
/// </summary>
internal static class Lp5Json
{
    public static JToken Get(JToken token, string key) =>
        token is JObject obj && obj.TryGetValue(key, out JToken value) && value.Type != JTokenType.Null
            ? value
            : null;

    public static string Str(JToken token, string key)
    {
        JToken value = Get(token, key);
        return value is JValue ? value.ToString() : null;
    }

    public static JObject Obj(JToken token, string key) => Get(token, key) as JObject;

    public static IReadOnlyList<JToken> Arr(JToken token, string key) =>
        Get(token, key) is JArray array ? array.ToList() : [];

    public static IEnumerable<string> Strings(JToken token, string key) =>
        Arr(token, key).Select(t => t is JValue v && v.Type != JTokenType.Null ? v.ToString() : null);

    public static double Num(JToken token, string key, double fallback)
    {
        JToken value = Get(token, key);
        return value?.Type switch
        {
            JTokenType.Integer or JTokenType.Float => value.Value<double>(),
            JTokenType.String when double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => fallback
        };
    }

    public static bool Bool(JToken token, string key)
    {
        JToken value = Get(token, key);
        return value?.Type switch
        {
            JTokenType.Boolean => value.Value<bool>(),
            JTokenType.String => string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
