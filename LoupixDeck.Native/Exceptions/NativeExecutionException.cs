using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Exceptions;

public class NativeExecutionException : Exception
{
    public string LibraryName { get; }
    public string Function { get; }

    public NativeExecutionException(string libraryName, string function, string? message, Exception? innerException = null) : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);
        ArgumentException.ThrowIfNullOrEmpty(function);
        LibraryName = libraryName;
        Function = function;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static NativeExecutionExceptionThrowHelper For(string libraryName, string function) => new(libraryName, function);

    // This inline check + outline throw pattern
    // helps the JIT optimize the usual fastpath of nothing extra happening
    // while avoiding the overhead of throwing prep
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIf(string libraryName, string function, [DoesNotReturnIf(true)]  bool condition)
    {
        if (condition) Throw(libraryName, function);
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw(string libraryName, string function) => throw CreateForLastError(libraryName, function);
    }

    public static NativeExecutionException CreateForLastError(string libraryName, string function)
    {
        int platformErrno = Marshal.GetLastPInvokeError();
        return CreateForPInvokeError(libraryName, function, platformErrno);
    }

    public static NativeExecutionException CreateForPInvokeError(string libraryName, string function, int platformErrno)
    {
        string? platformMessage = Marshal.GetPInvokeErrorMessage(platformErrno);
        string genericMessage = $"Native call to '{libraryName}.{function}' failed with error code {platformErrno}";
        string errorMessage = platformMessage is not null ? $"{genericMessage}: {platformMessage}" : genericMessage;
        return new(libraryName, function, errorMessage) { HResult = platformErrno };
    }
}

public readonly struct NativeExecutionExceptionThrowHelper(string libraryName, string function)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void ThrowIf([DoesNotReturnIf(true)] bool condition) => NativeExecutionException.ThrowIf(libraryName, function, condition);
}