using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Native;

public static partial class User32
{
    public static partial class Messages
    {
        [LibraryImport(LibraryName)]
        [DefaultDllImportSearchPaths(SearchPath)]
        private static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        public static bool TryGetMessage(out MSG msg, IntPtr hWnd = default, uint wMsgFilterMin = 0, uint wMsgFilterMax = 0, bool throwOnError = true)
        {
            int result = GetMessage(out msg, hWnd, wMsgFilterMin, wMsgFilterMax);
            if (throwOnError)
                ThrowIf(result == -1, nameof(GetMessage));
            return result != 0;
        }

        /// <summary>
        /// Translates virtual-key messages into character messages.
        /// The character messages are posted to the calling thread's message queue,
        /// to be read the next time the thread calls the GetMessage or PeekMessage function.
        /// </summary>
        /// <param name="lpMsg">A reference to an MSG structure that contains message information retrieved from the calling thread's message queue by using the GetMessage or PeekMessage function.</param>
        /// <returns>
        /// <para>
        /// If the message is translated (that is, a character message is posted to the thread's message queue), the return value is <see langword="true">.
        /// </para>
        /// <para>
        /// If the message is WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, or WM_SYSKEYUP, the return value is <see langword="true">, regardless of the translation.
        /// </para>
        /// <para>
        /// If the message is not translated (that is, a character message is not posted to the thread's message queue), the return value is <see langword="false">.
        /// </para>
        /// </returns>
        [LibraryImport(LibraryName)]
        [DefaultDllImportSearchPaths(SearchPath)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TranslateMessage(in MSG lpMsg);

        /// <summary>
        /// Dispatches a message to a window procedure.
        /// It is typically used to dispatch a message retrieved by the GetMessage function.
        /// </summary>
        /// <param name="lpMsg">A pointer to a structure that contains the message.</param>
        /// <returns>
        /// The return value specifies the value returned by the window procedure.
        /// Although its meaning depends on the message being dispatched, the return value generally is ignored.
        /// </returns>
        [LibraryImport(LibraryName)]
        [DefaultDllImportSearchPaths(SearchPath)]
        public static partial IntPtr DispatchMessage(in MSG lpMsg);

        /// <summary>
        /// Posts a message to the message queue of the specified thread.
        /// It returns without waiting for the thread to process the message.
        /// </summary>
        /// <remarks>
        /// See <see href="https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postthreadmessagea">Microslop's documentation</see> for more details
        /// </remarks>
        /// <param name="idThread">The identifier of the thread to which the message is to be posted.</param>
        /// <param name="msg">The type of message to be posted.</param>
        /// <param name="wParam">Additional message-specific information.</param>
        /// <param name="lParam">Additional message-specific information.</param>
        /// <returns></returns>
        [LibraryImport(LibraryName)]
        [DefaultDllImportSearchPaths(SearchPath)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
