using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native;

public static partial class Kernel32
{
    /// <summary>
    /// Allocates a new console for the calling process.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the function succeeds, else <see langword="false"/>.
    /// To get extended error information, call <see cref="Marshal.GetLastWin32Error"/>
    /// </returns>
    /// <remarks>
    /// A process can be associated with only one console, so <see cref="AllocConsole"/> fails if the calling process already has a console.
    /// A process can use the <see href="https://learn.microsoft.com/en-us/windows/console/FreeConsole">FreeConsole</see> function to detach itself from its current console,
    /// then it can call <see cref="AllocConsole"/> to create a new console or <see cref="AttachConsole"/> to attach to another console.
    /// If the calling process creates a child process, the child inherits the new console.
    /// <see cref="AllocConsole"/> initializes standard input, standard output, and standard error handles for the new console.
    /// The standard input handle is a handle to the console's input buffer, and the standard output and standard error handles are handles to the console's screen buffer.
    /// To retrieve these handles, use the [**GetStdHandle**](getstdhandle.md) function.
    /// </remarks>
    [DefaultDllImportSearchPaths(SearchPath)]
    [LibraryImport(LibraryName, EntryPoint = "AllocConsole", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllocConsole();

    /// <summary>See reference information about the AttachConsole function, which attaches the calling process to the console of the specified process.</summary>
    /// <param name="processId">
    /// <para>The identifier of the process whose console is to be used. This parameter can be one of the following values. | Value | Meaning | |-|-| | *pid* | Use the console of the specified process. | | **ATTACH\_PARENT\_PROCESS** `(DWORD)-1` | Use the console of the parent of the current process. |</para>
    /// <para><see href="https://learn.microsoft.com/windows/console/attachconsole#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero. To get extended error information, call [**GetLastError**](/windows/win32/api/errhandlingapi/nf-errhandlingapi-getlasterror).</para>
    /// </returns>
    /// <remarks>
    /// <para>A process can be attached to at most one console. If the calling process is already attached to a console, the error code returned is **ERROR\_ACCESS\_DENIED**. If the specified process does not have a console, the error code returned is **ERROR\_INVALID\_HANDLE**. If the specified process does not exist, the error code returned is **ERROR\_INVALID\_PARAMETER**. A process can use the [**FreeConsole**](freeconsole.md) function to detach itself from its console. If other processes share the console, the console is not destroyed, but the process that called **FreeConsole** cannot refer to it. A console is closed when the last process attached to it terminates or calls **FreeConsole**. After a process calls **FreeConsole**, it can call the [<see cref="AllocConsole"/>](allocconsole.md) function to create a new console or **AttachConsole** to attach to another console. This function is primarily useful to applications that were linked with [**/SUBSYSTEM:WINDOWS**](/cpp/build/reference/subsystem-specify-subsystem), which implies to the operating system that a console is not needed before entering the program's main method. In that instance, the standard handles retrieved with [**GetStdHandle**](getstdhandle.md) will likely be invalid on startup until **AttachConsole** is called. The exception to this is if the application is launched with handle inheritance by its parent process. To compile an application that uses this function, define **\_WIN32\_WINNT** as `0x0501` or later. For more information, see [Using the Windows Headers](/windows/win32/winprog/using-the-windows-headers).</para>
    /// <para><see href="https://learn.microsoft.com/windows/console/attachconsole#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [DefaultDllImportSearchPaths(SearchPath)]
    [LibraryImport(LibraryName, EntryPoint = "AttachConsole", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAttachConsoleToProcessParent() => AttachConsole(unchecked((uint)-1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AttachConsoleToProcessParent()
    {
        bool success = TryAttachConsoleToProcessParent();
        ThrowIf(!success, nameof(AttachConsole));
    }
}