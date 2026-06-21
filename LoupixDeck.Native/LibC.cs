using System.Runtime.Versioning;

namespace LoupixDeck.Native;

[SupportedOSPlatform("linux")]
public static partial class LibC
{
    private const string LibraryName = "libc";
}
