using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Linux;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UInputDeviceStruct
{
    [InlineArray(Length)]
    private struct CharArray80
    {
        public const int Length = 80;
        public char c;
        public static Span<char> CreateSpan(ref CharArray80 array) => MemoryMarshal.CreateSpan(ref array.c, Length);
    }

    [InlineArray(Length)]
    private struct IntArray64
    {
        public const int Length = 64;
        public int i;
        public static Span<int> CreateSpan(ref IntArray64 array) => MemoryMarshal.CreateSpan(ref array.i, Length);
    }

    public static UInputDeviceStruct Default() => new()
    {
        Name = "LoupixVirtualKeyboard",
        id_bustype = 0,
        id_vendor = 0x1234,
        id_product = 0x5678,
        id_version = 1,
    };

    private CharArray80 name;
    public ushort id_bustype;
    public ushort id_vendor;
    public ushort id_product;
    public ushort id_version;
    public int ff_effects_max;
    private IntArray64 absmax;
    private IntArray64 absmin;
    private IntArray64 absfuzz;
    private IntArray64 absflat;

    [UnscopedRef]
    private Span<char> NameSpan => CharArray80.CreateSpan(ref name);
    public string Name
    {
        get => new(NameSpan);
        set
        {
            if (value.Length > 80)
                throw new ArgumentException("Device name cannot exceed 80 characters.", nameof(value));
            value.CopyTo(NameSpan);
        }
    }
    [UnscopedRef]
    public Span<int> AbsMaxSpan => IntArray64.CreateSpan(ref absmax);
    [UnscopedRef]
    public Span<int> AbsMinSpan => IntArray64.CreateSpan(ref absmin);
    [UnscopedRef]
    public Span<int> AbsFuzzSpan => IntArray64.CreateSpan(ref absfuzz);
    [UnscopedRef]
    public Span<int> AbsFlatSpan => IntArray64.CreateSpan(ref absflat);
}
