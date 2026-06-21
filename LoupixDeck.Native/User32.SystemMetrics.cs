using System.Runtime.InteropServices;

namespace LoupixDeck.Native;

public static partial class User32
{
    public static partial class SystemMetrics
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Matching Windows API")]
        private enum SYSTEM_METRICS_INDEX
        {
            SM_ARRANGE = 56,
            SM_CLEANBOOT = 67,
            SM_CMONITORS = 80,
            SM_CMOUSEBUTTONS = 43,
            SM_CONVERTIBLESLATEMODE = 8195,
            SM_CXBORDER = 5,
            SM_CXCURSOR = 13,
            SM_CXDLGFRAME = 7,
            SM_CXDOUBLECLK = 36,
            SM_CXDRAG = 68,
            SM_CXEDGE = 45,
            SM_CXFIXEDFRAME = 7,
            SM_CXFOCUSBORDER = 83,
            SM_CXFRAME = 32,
            SM_CXFULLSCREEN = 16,
            SM_CXHSCROLL = 21,
            SM_CXHTHUMB = 10,
            SM_CXICON = 11,
            SM_CXICONSPACING = 38,
            SM_CXMAXIMIZED = 61,
            SM_CXMAXTRACK = 59,
            SM_CXMENUCHECK = 71,
            SM_CXMENUSIZE = 54,
            SM_CXMIN = 28,
            SM_CXMINIMIZED = 57,
            SM_CXMINSPACING = 47,
            SM_CXMINTRACK = 34,
            SM_CXPADDEDBORDER = 92,
            SM_CXSCREEN = 0,
            SM_CXSIZE = 30,
            SM_CXSIZEFRAME = 32,
            SM_CXSMICON = 49,
            SM_CXSMSIZE = 52,
            SM_CXVIRTUALSCREEN = 78,
            SM_CXVSCROLL = 2,
            SM_CYBORDER = 6,
            SM_CYCAPTION = 4,
            SM_CYCURSOR = 14,
            SM_CYDLGFRAME = 8,
            SM_CYDOUBLECLK = 37,
            SM_CYDRAG = 69,
            SM_CYEDGE = 46,
            SM_CYFIXEDFRAME = 8,
            SM_CYFOCUSBORDER = 84,
            SM_CYFRAME = 33,
            SM_CYFULLSCREEN = 17,
            SM_CYHSCROLL = 3,
            SM_CYICON = 12,
            SM_CYICONSPACING = 39,
            SM_CYKANJIWINDOW = 18,
            SM_CYMAXIMIZED = 62,
            SM_CYMAXTRACK = 60,
            SM_CYMENU = 15,
            SM_CYMENUCHECK = 72,
            SM_CYMENUSIZE = 55,
            SM_CYMIN = 29,
            SM_CYMINIMIZED = 58,
            SM_CYMINSPACING = 48,
            SM_CYMINTRACK = 35,
            SM_CYSCREEN = 1,
            SM_CYSIZE = 31,
            SM_CYSIZEFRAME = 33,
            SM_CYSMCAPTION = 51,
            SM_CYSMICON = 50,
            SM_CYSMSIZE = 53,
            SM_CYVIRTUALSCREEN = 79,
            SM_CYVSCROLL = 20,
            SM_CYVTHUMB = 9,
            SM_DBCSENABLED = 42,
            SM_DEBUG = 22,
            SM_DIGITIZER = 94,
            SM_IMMENABLED = 82,
            SM_MAXIMUMTOUCHES = 95,
            SM_MEDIACENTER = 87,
            SM_MENUDROPALIGNMENT = 40,
            SM_MIDEASTENABLED = 74,
            SM_MOUSEPRESENT = 19,
            SM_MOUSEHORIZONTALWHEELPRESENT = 91,
            SM_MOUSEWHEELPRESENT = 75,
            SM_NETWORK = 63,
            SM_PENWINDOWS = 41,
            SM_REMOTECONTROL = 8193,
            SM_REMOTESESSION = 4096,
            SM_SAMEDISPLAYFORMAT = 81,
            SM_SECURE = 44,
            SM_SERVERR2 = 89,
            SM_SHOWSOUNDS = 70,
            SM_SHUTTINGDOWN = 8192,
            SM_SLOWMACHINE = 73,
            SM_STARTER = 88,
            SM_SWAPBUTTON = 23,
            SM_SYSTEMDOCKED = 8196,
            SM_TABLETPC = 86,
            SM_XVIRTUALSCREEN = 76,
            SM_YVIRTUALSCREEN = 77,
        }

        /// <summary>
        /// Retrieves the specified system metric or system configuration setting.
        /// </summary>
        /// <param name="nIndex">Type: <b>int</b></param>
        /// <returns>
        /// If the function succeeds, the return value is the requested system metric or configuration setting. If the function fails, the return value is 0. <a href="https://docs.microsoft.com/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a> does not provide extended error information.</para>
        /// </returns>
        /// <remarks>
        /// <para>
        /// System metrics can vary from display to display.
        /// <b>GetSystemMetrics</b>(SM_CMONITORS) counts only visible display monitors.
        /// This is different from <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-enumdisplaymonitors">EnumDisplayMonitors</a>,
        /// which enumerates both visible display monitors and invisible  pseudo-monitors that are associated with mirroring drivers.
        /// An invisible pseudo-monitor is associated with a pseudo-device used to mirror application drawing for remoting or other purposes.
        /// The SM_ARRANGE setting specifies how the system arranges minimized windows, and consists of a starting position and a direction.
        /// The starting position can be one of the following values.</para>
        /// <para>
        /// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getsystemmetrics#">Read more on learn.microsoft.com</see>.
        /// </para>
        /// </remarks>
        [LibraryImport(LibraryName)]
        [DefaultDllImportSearchPaths(SearchPath)]
        private static partial int GetSystemMetrics(SYSTEM_METRICS_INDEX nIndex);

        public static int VirtualScreenLeft => GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        public static int VirtualScreenTop => GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        public static int VirtualScreenWidth => GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        public static int VirtualScreenHeight => GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
    }
}

