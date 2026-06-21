using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LoupixDeck.Native.Exceptions;

namespace LoupixDeck.Native;

[SupportedOSPlatform("windows")]
public static partial class Kernel32
{
    private const string LibraryName = "KERNEL32.dll";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.System32;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ThrowIf([DoesNotReturnIf(true)] bool condition, [CallerMemberName] string caller = null!) => NativeExecutionException.ThrowIf(LibraryName, caller, condition);

}