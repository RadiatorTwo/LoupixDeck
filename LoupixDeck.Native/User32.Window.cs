using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Native;

public static partial class User32
{
    /// <summary>Retrieves the identifier of the thread that created the specified window and, optionally, the identifier of the process that created the window.</summary>
    /// <param name="hWnd">
    /// <para>Type: <b>HWND</b> A handle to the window.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpdwProcessId">
    /// <para>A pointer to a variable that receives the process identifier. If this parameter is not <b>NULL</b>, <b>GetWindowThreadProcessId</b> copies the identifier of the process to the variable; otherwise, it does not. If the function fails, the value of the variable is unchanged.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// If the function succeeds, the return value is the identifier of the thread that created the window.
    /// If the window handle is invalid, the return value is zero. To get extended error information, call <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid">Learn more about this API from learn.microsoft.com</see>.</para>
    /// </remarks>
    [DefaultDllImportSearchPaths(SearchPath)]
    [LibraryImport(LibraryName, EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, [Optional] ref uint lpdwProcessId);
    public static bool TryGetWindowThreadId(WindowHandle hWnd, out uint threadId)
    {
        threadId = GetWindowThreadProcessId(hWnd, ref Unsafe.NullRef<uint>());
        return threadId != 0;
    }

    public static bool TryGetWindowThreadId(WindowHandle hWnd, out uint threadId, out uint processId)
    {
        processId = 0;
        threadId = GetWindowThreadProcessId(hWnd, ref processId);
        return threadId != 0;
    }

    public static bool TryGetWindowProcessId(WindowHandle hWnd, out uint processId) => TryGetWindowThreadId(hWnd, out uint threadId, out processId) && processId != 0;

    /// <summary>Retrieves the length, in characters, of the specified window's title bar text (if the window has a title bar). (Unicode)</summary>
    /// <param name="hWnd">
    /// <para>Type: <b>HWND</b> A handle to the window or control.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextlengthw#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>int</b> If the function succeeds, the return value is the length, in characters, of the text. Under certain conditions, this value might be greater than the length of the text (see Remarks). If the window has no text, the return value is zero. Function failure is indicated by a return value of zero and a <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a> result that is nonzero. > [!NOTE] > This function does not clear the most recent error information. To determine success or failure, clear the most recent error information by calling <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-setlasterror">SetLastError</a> with 0, then call <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para>If the target window is owned by the current process, <b>GetWindowTextLength</b> causes a <a href="https://docs.microsoft.com/windows/desktop/winmsg/wm-gettextlength">WM_GETTEXTLENGTH</a> message to be sent to the specified window or control. Under certain conditions, the <b>GetWindowTextLength</b> function may return a value that is larger than the actual length of the text. This occurs with certain mixtures of ANSI and Unicode, and is due to the system allowing for the possible existence of double-byte character set (DBCS) characters within the text. The return value, however, will always be at least as large as the actual length of the text; you can thus always use it to guide buffer allocation. This behavior can occur when an application uses both ANSI functions and common dialogs, which use Unicode. It can also occur when an application uses the ANSI version of <b>GetWindowTextLength</b> with a window whose window procedure is Unicode, or the Unicode version of <b>GetWindowTextLength</b> with a window whose window procedure is ANSI. For more information on ANSI and ANSI functions, see <a href="https://docs.microsoft.com/windows/desktop/Intl/conventions-for-function-prototypes">Conventions for Function Prototypes</a>. To obtain the exact length of the text, use the <a href="https://docs.microsoft.com/windows/desktop/winmsg/wm-gettext">WM_GETTEXT</a>, <a href="https://docs.microsoft.com/windows/desktop/Controls/lb-gettext">LB_GETTEXT</a>, or <a href="https://docs.microsoft.com/windows/desktop/Controls/cb-getlbtext">CB_GETLBTEXT</a> messages, or the <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-getwindowtexta">GetWindowText</a> function.</para>
    /// <para>> [!NOTE] > The winuser.h header defines GetWindowTextLength as an alias which automatically selects the ANSI or Unicode version of this function based on the definition of the UNICODE preprocessor constant. Mixing usage of the encoding-neutral alias with code that not encoding-neutral can lead to mismatches that result in compilation or runtime errors. For more information, see [Conventions for Function Prototypes](/windows/win32/intl/conventions-for-function-prototypes).</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextlengthw#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [DefaultDllImportSearchPaths(SearchPath)]
    [LibraryImport(LibraryName, EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// Copies the text of the specified window's title bar (if it has one) into a buffer.
    /// If the specified window is a control, the text of the control is copied.
    /// However, GetWindowText cannot retrieve the text of a control in another application. (Unicode)
    /// </summary>
    /// <param name="hWnd">A handle to the window or control containing the text.</param>
    /// <param name="lpString">
    /// <para>Type: <b>LPTSTR</b> The buffer that will receive the text. If the string is as long or longer than the buffer, the string is truncated and terminated with a null character.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextw#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <param name="nMaxCount">
    /// <para>Type: <b>int</b> The maximum number of characters to copy to the buffer, including the null character. If the text exceeds this limit, it is truncated.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextw#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// If the function succeeds, the return value is the length, in characters, of the copied string, not including the terminating null character.
    /// If the window has no title bar or text, if the title bar is empty, or if the window or control handle is invalid, the return value is zero.
    /// To get extended error information, call <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>. This function cannot retrieve the text of an edit control in another application.</para>
    /// </returns>
    /// <remarks>
    /// <para>If the target window is owned by the current process, <b>GetWindowText</b> causes a <a href="https://docs.microsoft.com/windows/desktop/winmsg/wm-gettext">WM_GETTEXT</a> message to be sent to the specified window or control.
    /// If the target window is owned by another process and has a caption, <b>GetWindowText</b> retrieves the window caption text.
    /// If the window does not have a caption, the return value is a null string. This behavior is by design. It allows applications to call <b>GetWindowText</b> without becoming unresponsive if the process that owns the target window is not responding. However, if the target window is not responding and it belongs to the calling application, <b>GetWindowText</b> will cause the calling application to become unresponsive. To retrieve the text of a control in another process, send a <a href="https://docs.microsoft.com/windows/desktop/winmsg/wm-gettext">WM_GETTEXT</a> message directly instead of calling <b>GetWindowText</b>.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowtextw#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [DefaultDllImportSearchPaths(SearchPath)]
    [LibraryImport(LibraryName, EntryPoint = "GetWindowTextW", SetLastError = true)]
    private static partial int GetWindowText(IntPtr hWnd, Span<char> lpString, int nMaxCount);

    public static string GetWindowText(WindowHandle hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            ThrowIf(Marshal.GetLastPInvokeError() is not 0);
            return string.Empty;
        }

        char[]? rented = null;
        int lengthWithTerminator = length + 1; // could tack on extra buffer
        Span<char> buffer = length < 64 ? stackalloc char[lengthWithTerminator] : ((rented = ArrayPool<char>.Shared.Rent(lengthWithTerminator)).AsSpan(0, lengthWithTerminator));
        try
        {

            int resultLength = GetWindowText(hWnd, buffer, buffer.Length);
            ThrowIf(resultLength is <= 0);
            return buffer[..resultLength].ToString();
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

}
