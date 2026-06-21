using LoupixDeck.Native.Types.Windows;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native;

public static partial class User32
{
    public static partial class Input
    {
        [LibraryImport(LibraryName, EntryPoint = "SendInput", SetLastError = true)]
        [DefaultDllImportSearchPaths(SearchPath)]
        private static partial uint SendInput(uint nInputs, ReadOnlySpan<INPUT> inputs, int cbSize);

        [LibraryImport(LibraryName, EntryPoint = "SendInput", SetLastError = true)]
        [DefaultDllImportSearchPaths(SearchPath)]
        private static partial uint SendInput(uint nInputs, ReadOnlySpan<MOUSEINPUT> inputs, int cbSize);

        [LibraryImport(LibraryName, EntryPoint = "SendInput", SetLastError = true)]
        [DefaultDllImportSearchPaths(SearchPath)]
        private static partial uint SendInput(uint nInputs, ReadOnlySpan<KEYBDINPUT> inputs, int cbSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(ReadOnlySpan<INPUT> inputs)
            => inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, inputs, sizeof(INPUT));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(ReadOnlySpan<MOUSEINPUT> inputs)
            => inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, inputs, sizeof(MOUSEINPUT));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(ReadOnlySpan<KEYBDINPUT> inputs)
            => inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, inputs, sizeof(KEYBDINPUT));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(INPUT input)
            => SendInput(1, MemoryMarshal.CreateReadOnlySpan(ref input, 1), sizeof(INPUT));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(MOUSEINPUT input)
            => SendInput(1, MemoryMarshal.CreateReadOnlySpan(ref input, 1), sizeof(MOUSEINPUT));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint SendInput(KEYBDINPUT input)
            => SendInput(1, MemoryMarshal.CreateReadOnlySpan(ref input, 1), sizeof(KEYBDINPUT));
    }
}
