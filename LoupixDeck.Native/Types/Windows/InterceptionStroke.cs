using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Windows;

[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct InterceptionStroke
{
    [FieldOffset(0)] public InterceptionMouseStroke Mouse;
    [FieldOffset(0)] public InterceptionKeyStroke Key;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator InterceptionStroke(InterceptionMouseStroke mouse) => new() { Mouse = mouse };
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator InterceptionStroke(InterceptionKeyStroke key) => new() { Key = key };
}

[StructLayout(LayoutKind.Sequential, Size = 20)]
public struct InterceptionMouseStroke
{
    public ushort State;
    public ushort Flags;
    public short Rolling;
    public int X;
    public int Y;
    public uint Information;
}

// Native InterceptionStroke is a byte blob sized to the larger of the key/mouse strokes
// (the mouse stroke = 20 bytes). For a keyboard device the driver reads the first 8 bytes
// as an InterceptionKeyStroke { ushort code; ushort state; uint information; }. Size = 20
// is mandatory so interception_send copies the right number of bytes per stroke.
[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct InterceptionKeyStroke
{
    [FieldOffset(0)] public ushort Code;
    [FieldOffset(2)] public ushort State;
    [FieldOffset(4)] public uint Information;
}
