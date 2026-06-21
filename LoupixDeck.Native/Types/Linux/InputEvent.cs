using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Linux;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = ByteSize)]
internal struct InputEvent
{
    public const int ByteSize = TimeVal.ByteSize + (sizeof(ushort) * 2) + sizeof(int);

    public TimeVal time;
    public ushort type;
    public ushort code;
    public int value;
}
