namespace LoupixDeck.Services.Commands;

/// <summary>
/// Produces placeholder default values for command parameters that the user
/// has not supplied yet (used by the command builder when materializing a
/// command string from a menu selection).
/// </summary>
public static class ParameterDefaults
{
    public static object GetDefaultValue(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return false;
        if (type == typeof(char)) return '\0';
        if (type == typeof(byte)) return (byte)0;
        if (type == typeof(sbyte)) return (sbyte)0;
        if (type == typeof(short)) return (short)0;
        if (type == typeof(ushort)) return (ushort)0;
        if (type == typeof(int)) return 0;
        if (type == typeof(uint)) return 0U;
        if (type == typeof(long)) return 0L;
        if (type == typeof(ulong)) return 0UL;
        if (type == typeof(float)) return 0f;
        if (type == typeof(double)) return 0.0;
        if (type == typeof(decimal)) return 0m;
        if (type == typeof(DateTime)) return default(DateTime);
        if (type == typeof(Guid)) return Guid.Empty;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0
                ? values.GetValue(0)
                : Activator.CreateInstance(type);
        }

        if (Nullable.GetUnderlyingType(type) != null)
            return null;

        if (type.IsValueType)
            return Activator.CreateInstance(type);

        return null;
    }
}
