#nullable enable
using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Native;

public static partial class User32
{
    private enum MAP_VIRTUAL_KEY_TYPE : uint
    {
        MAPVK_VK_TO_VSC = 0U,

        /// <summary>
        /// <para>
        /// The uCode parameter is a scan code and is translated into a virtual-key code that does not distinguish between left- and right-hand keys.
        /// </para>
        /// <para>
        /// If there is no translation, the function returns 0.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Windows Vista and later: the high byte of the uCode value can contain either 0xe0 or 0xe1 to specify the extended scan code.
        /// </remarks>
        MAPVK_VSC_TO_VK = 1U,

        /// <summary>
        /// <para>
        /// </para>
        /// The uCode parameter is a virtual-key code and is translated into an unshifted character value in the low order word of the return value.
        /// Dead keys (diacritics) are indicated by setting the top bit of the return value.
        /// If there is no translation, the function returns 0. See Remarks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// To specify a handle to the keyboard layout to use for translating the specified code, use the <see cref="MapVirtualKeyEx"/> function.
        /// </para>
        /// <para>
        /// An application can use MapVirtualKey to translate scan codes to the virtual-key code constants
        /// <see cref="VIRTUAL_KEY.VK_SHIFT"/>, <see cref="VIRTUAL_KEY.VK_CONTROL"/>, and <see cref="VIRTUAL_KEY.VK_MENU"/>, and vice versa.
        /// </para>
        /// <para>
        /// These translations do not distinguish between the left and right instances of the SHIFT, CTRL, or ALT keys.
        /// </para>
        /// 
        /// <para>
        /// </para>
        /// An application can get the scan code corresponding to the left or right instance of one of these keys by calling MapVirtualKey with
        /// uCode set to one of the following virtual-key code constants:
        /// 
        /// <list type="bullet">
        ///   <item><see cref="VIRTUAL_KEY.VK_LSHIFT"/></item>
        ///   <item><see cref="VIRTUAL_KEY.VK_RSHIFT"/></item>
        ///   <item><see cref="VIRTUAL_KEY.VK_LCONTROL"/></item>
        ///   <item><see cref="VIRTUAL_KEY.VK_RCONTROL"/></item>
        ///   <item><see cref="VIRTUAL_KEY.VK_LMENU"/></item>
        ///   <item><see cref="VIRTUAL_KEY.VK_RMENU"/></item>
        /// </list>
        /// 
        /// <para>
        /// These left- and right-distinguishing constants are available to an application only through the
        /// GetKeyboardState, SetKeyboardState, GetAsyncKeyState, GetKeyState, MapVirtualKey, and MapVirtualKeyEx functions.
        /// For list complete table of virtual key codes, see Virtual Key Codes.
        /// </para>
        /// 
        /// <para>
        /// In MAPVK_VK_TO_CHAR mode virtual-key codes, the 'A'..'Z' keys are translated to upper-case 'A'..'Z' characters regardless of current keyboard layout. If you want to translate a virtual-key code to the corresponding character, use the ToAscii function.
        /// </para>
        /// </remarks>
        MAPVK_VK_TO_CHAR = 2U,

        // MapVirtualKey translation type: scan code → virtual key, distinguishing left/right
        // modifier keys and honouring the extended-key (E0) prefix.
        MAPVK_VSC_TO_VK_EX = 3U,

        /// <summary>
        /// Windows Vista and later: The uCode parameter is a virtual-key code and is translated into a scan code.
        /// 
        /// If it is a virtual-key code that does not distinguish between left- and right-hand keys, the left-hand scan code is returned.
        /// 
        /// If the scan code is an extended scan code, the high byte of the returned value will contain either 0xe0 or 0xe1 to specify the extended scan code.
        /// 
        /// If there is no translation, the function returns 0.
        /// </summary>
        MAPVK_VK_TO_VSC_EX = 4U,
    }

    /// <summary>
    /// Translates (maps) a virtual-key code into a scan code or character value, or translates a scan code into a virtual-key code. (Unicode)
    /// </summary>
    /// <param name="uCode">
    /// <para>Type: **UINT** The [virtual key code](/windows/desktop/inputdev/virtual-key-codes) or scan code for a key. How this value is interpreted depends on the value of the *uMapType* parameter. **Starting with Windows Vista**, the high byte of the *uCode* value can contain either 0xe0 or 0xe1 to specify the extended scan code.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <param name="uMapType">
    /// <para>Type: **UINT** The translation to be performed. The value of this parameter depends on the value of the *uCode* parameter. | Value | Meaning | |-------|---------| | **MAPVK\_VK\_TO\_VSC**<br>0 | The *uCode* parameter is a virtual-key code and is translated into a scan code. If it is a virtual-key code that does not distinguish between left- and right-hand keys, the left-hand scan code is returned. If there is no translation, the function returns 0. | | **MAPVK\_VSC\_TO\_VK**<br>1 | The *uCode* parameter is a scan code and is translated into a virtual-key code that does not distinguish between left- and right-hand keys. If there is no translation, the function returns 0. | | **MAPVK\_VK\_TO\_CHAR**<br>2 | The *uCode* parameter is a virtual-key code and is translated into an unshifted character value in the low order word of the return value. Dead keys (diacritics) are indicated by setting the top bit of the return value. If there is no translation, the function returns 0. See Remarks. | | **MAPVK\_VSC\_TO\_VK\_EX**<br>3 | The *uCode* parameter is a scan code and is translated into a virtual-key code that distinguishes between left- and right-hand keys. If there is no translation, the function returns 0. | | **MAPVK\_VK\_TO\_VSC\_EX**<br>4 | **Windows Vista and later:** The *uCode* parameter is a virtual-key code and is translated into a scan code. If it is a virtual-key code that does not distinguish between left- and right-hand keys, the left-hand scan code is returned. If the scan code is an extended scan code, the high byte of the *uCode* value can contain either 0xe0 or 0xe1 to specify the extended scan code. If there is no translation, the function returns 0. |</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw#parameters">Read more on learn.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: **UINT** The return value is either a scan code, a virtual-key code, or a character value, depending on the value of *uCode* and *uMapType*. If there is no translation, the return value is zero.</para>
    /// </returns>
    /// <remarks>
    /// <para>To specify a handle to the keyboard layout to use for translating the specified code, use the [MapVirtualKeyEx](nf-winuser-mapvirtualkeyexw.md) function. An application can use **MapVirtualKey** to translate scan codes to the virtual-key code constants **VK_SHIFT**, **VK_CONTROL**, and **VK_MENU**, and vice versa. These translations do not distinguish between the left and right instances of the SHIFT, CTRL, or ALT keys. An application can get the scan code corresponding to the left or right instance of one of these keys by calling **MapVirtualKey** with *uCode* set to one of the following virtual-key code constants: - **VK\_LSHIFT** - **VK\_RSHIFT** - **VK\_LCONTROL** - **VK\_RCONTROL** - **VK\_LMENU** - **VK\_RMENU** These left- and right-distinguishing constants are available to an application only through the [GetKeyboardState](nf-winuser-getkeyboardstate.md), [SetKeyboardState](nf-winuser-setkeyboardstate.md), [GetAsyncKeyState](nf-winuser-getasynckeystate.md), [GetKeyState](nf-winuser-getkeystate.md), [MapVirtualKey](nf-winuser-mapvirtualkeyw.md), and **MapVirtualKeyEx** functions. For list complete table of virtual key codes, see [Virtual Key Codes](/windows/win32/inputdev/virtual-key-codes). In **MAPVK\_VK\_TO\_CHAR** mode [virtual-key codes](/windows/win32/inputdev/virtual-key-codes), the 'A'..'Z' keys are translated to upper-case 'A'..'Z' characters regardless of current keyboard layout. If you want to translate a virtual-key code to the corresponding character, use the [ToUnicode](/windows/win32/api/winuser/nf-winuser-tounicode) function. > [!NOTE] > The winuser.h header defines MapVirtualKey as an alias which automatically selects the ANSI or Unicode version of this function based on the definition of the UNICODE preprocessor constant. Mixing usage of the encoding-neutral alias with code that not encoding-neutral can lead to mismatches that result in compilation or runtime errors. For more information, see [Conventions for Function Prototypes](/windows/win32/intl/conventions-for-function-prototypes).</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw#">Read more on learn.microsoft.com</see>.</para>
    /// </remarks>
    [LibraryImport(LibraryName, EntryPoint = "MapVirtualKeyW"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint MapVirtualKey(uint uCode, MAP_VIRTUAL_KEY_TYPE uMapType);

    /// <summary>
    /// Maps a scan code into a virtual-key code that distinguishes between left- and right-hand keys.
    /// </summary>
    /// <remarks>
    /// Windows Vista and later: the high byte of the uCode value can contain either 0xe0 or 0xe1 to specify the extended scan code.
    /// </remarks>
    /// <param name="uCode">Keyboard scan code</param>
    /// <returns>
    /// <para>The translated into virtual-key code which distinguishes between left- and right-hand keys.</para>
    /// <para>If there is no translation, the function returns 0.</para>
    /// </returns>
    public static uint MapVirtualScanCodeToVirtualKeyEx(uint uCode) => MapVirtualKey(uCode, MAP_VIRTUAL_KEY_TYPE.MAPVK_VSC_TO_VK_EX);

    /// <summary>
    /// <para>
    /// The uCode parameter is a virtual-key code and is translated into a scan code.
    /// </para>
    /// <para>
    /// If it is a virtual-key code that does not distinguish between left-and right-hand keys, the left-hand scan code is returned.
    /// </para>
    /// <para>
    /// If there is no translation, the function returns 0.
    /// </para>
    /// </summary>
    /// <summary>
    /// 
    /// </summary>
    /// <param name="uCode"></param>
    /// <returns>
    /// <para>The virtual scan code for the specified virtual-key code, if there are left-right distinctions, the left-hand scan code is returned.</para>
    /// <para>If there is no translation, the function returns 0.</para>
    /// </returns>
    public static ushort MapVirtualKeyToToVirtualScanCode(uint uCode) => (ushort)MapVirtualKey(uCode, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
}
