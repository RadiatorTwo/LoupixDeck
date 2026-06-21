using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LoupixDeck.Native.Exceptions;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Native;

[SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "It's the point of these classes")]
public static partial class User32
{
    private const string LibraryName = "USER32.dll";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.System32;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ThrowIf([DoesNotReturnIf(true)] bool condition, [CallerMemberName] string caller = null!) => NativeExecutionException.ThrowIf(LibraryName, caller, condition);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle([Optional] string? lpModuleName);

    public static IntPtr GetModuleHandleSelf() => GetModuleHandle(null);

    /// <summary>
    /// Determines whether a key is up or down at the time the function is called, and whether the key was pressed after a previous call to GetAsyncKeyState.
    /// </summary>
    /// <param name="vKey">
    /// <para>Type: <b>int</b> The virtual-key code. For more information, see <a href="https://docs.microsoft.com/windows/desktop/inputdev/virtual-key-codes">Virtual Key Codes</a>. You can use left- and right-distinguishing constants to specify certain keys.
    /// See the Remarks section for further information.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getasynckeystate#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>SHORT</b> If the function succeeds, the return value specifies whether the key was pressed since the last call to <b>GetAsyncKeyState</b>, and whether the key is currently up or down. If the most significant bit is set, the key is down, and if the least significant bit is set, the key was pressed after the previous call to <b>GetAsyncKeyState</b>. However, you should not rely on this last behavior; for more information, see the Remarks. The return value is zero for the following cases: </para>
    /// <para>This doc was truncated.</para>
    /// </returns>
    /// <remarks>
    /// <para>The <b>GetAsyncKeyState</b> function works with mouse buttons. However, it checks on the state of the physical mouse buttons, not on the logical mouse buttons that the physical buttons are mapped to. For example, the call <b>GetAsyncKeyState</b>(VK_LBUTTON) always returns the state of the left physical mouse button, regardless of whether it is mapped to the left or right logical mouse button. You can determine the system's current mapping of physical mouse buttons to logical mouse buttons by calling <c>GetSystemMetrics(SM_SWAPBUTTON)</c>. which returns TRUE if the mouse buttons have been swapped. Although the least significant bit of the return value indicates whether the key has been pressed since the last query, due to the preemptive multitasking nature of Windows, another application can call <b>GetAsyncKeyState</b> and receive the "recently pressed" bit instead of your application. The behavior of the least significant bit of the return value is retained strictly for compatibility with 16-bit Windows applications (which are non-preemptive) and should not be relied upon. You can use the virtual-key code constants <b>VK_SHIFT</b>, <b>VK_CONTROL</b>, and <b>VK_MENU</b> as values for the <i>vKey</i> parameter. This gives the state of the SHIFT, CTRL, or ALT keys without distinguishing between left and right. You can use the following virtual-key code constants as values for <i>vKey</i> to distinguish between the left and right instances of those keys. </para>
    /// <para>This doc was truncated.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getasynckeystate#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [LibraryImport(LibraryName), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial short GetAsyncKeyState(int vKey);
    public static short GetAsyncKeyState(VIRTUAL_KEY vKey) => GetAsyncKeyState((int)vKey);

    public static bool AsyncKeyStateIsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;
    public static bool AsyncKeyStateIsDown(VIRTUAL_KEY vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    /// <summary>Retrieves the active input locale identifier (formerly called the keyboard layout).</summary>
    /// <param name="idThread">
    /// <para>Type: <b>DWORD</b> The identifier of the thread to query, or 0 for the current thread.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getkeyboardlayout#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>HKL</b> The return value is the input locale identifier for the thread. The low word contains a <a href="https://docs.microsoft.com/windows/desktop/Intl/language-identifiers">Language Identifier</a> for the input language and the high word contains a device handle to the physical layout of the keyboard.</para>
    /// </returns>
    /// <remarks>
    /// <para>The input locale identifier is a broader concept than a keyboard layout, since it can also encompass a speech-to-text converter, an Input Method Editor (IME), or any other form of input. Since the keyboard layout can be dynamically changed, applications that cache information about the current keyboard layout should process the <a href="https://docs.microsoft.com/windows/desktop/winmsg/wm-inputlangchange">WM_INPUTLANGCHANGE</a> message to be informed of changes in the input language. To get the KLID (keyboard layout ID) of the currently active HKL, call the  <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-getkeyboardlayoutnamea">GetKeyboardLayoutName</a>. <b>Beginning in Windows 8:</b> The preferred method to retrieve the language associated with the current keyboard layout or input method is a call to <a href="https://docs.microsoft.com/uwp/api/windows.globalization.language.currentinputmethodlanguagetag">Windows.Globalization.Language.CurrentInputMethodLanguageTag</a>. If your app passes language tags from <b>CurrentInputMethodLanguageTag</b> to any <a href="https://docs.microsoft.com/windows/desktop/Intl/national-language-support-functions">National Language Support</a> functions, it must first convert the tags by calling <a href="https://docs.microsoft.com/windows/desktop/api/winnls/nf-winnls-resolvelocalename">ResolveLocaleName</a>.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getkeyboardlayout#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [LibraryImport(LibraryName), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial UIntPtr GetKeyboardLayout(uint idThread);

    public static ushort GetKeyboardLayoutLanguageId(uint idThread = 0)
    {
        nuint fullValue = GetKeyboardLayout(idThread);
        // Low word of the HKL is the LANGID; primary language id lives in its low 10 bits.
        ushort langId = (ushort)(fullValue & 0xFF_FFU);
        return langId;
    }

    public static ushort GetPrimaryKeyboardLayoutLanguageId(uint idThread = 0) => (ushort)(GetKeyboardLayoutLanguageId(idThread) & 0x3FFU);
}
