using System.Runtime.InteropServices;

namespace LoupixDeck.Native;

public static partial class User32
{
    public static partial class Threads
    {
        [LibraryImport(LibraryName)]
        public static partial uint GetCurrentThreadId();
    }
}
