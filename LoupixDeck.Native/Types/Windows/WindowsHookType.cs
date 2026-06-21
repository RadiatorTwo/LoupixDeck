namespace LoupixDeck.Native.Types.Windows;

public enum WindowsHookType : int
{
    /// <summary>
    /// Installs a hook procedure that monitors messages before the system sends them to the destination window procedure.
    /// For more information, see the CallWndProc hook procedure.
    /// </summary>
    WH_CALLWNDPROC = 4,

    /// <summary>
    /// Installs a hook procedure that monitors messages after they have been processed by the destination window procedure.
    /// For more information, see the HOOKPROC callback function hook procedure.
    /// </summary>
    WH_CALLWNDPROCRET = 12,

    /// <summary>
    /// Installs a hook procedure that receives notifications useful to a CBT application.
    /// For more information, see the CBTProc hook procedure.
    /// </summary>
    WH_CBT = 5,

    /// <summary>
    /// Installs a hook procedure useful for debugging other hook procedures.
    /// For more information, see the DebugProc hook procedure.
    /// </summary>
    WH_DEBUG = 9,

    /// <summary>
    /// Installs a hook procedure that will be called when the application's foreground thread is about to become idle.
    /// This hook is useful for performing low priority tasks during idle time.
    /// For more information, see the ForegroundIdleProc hook procedure.
    /// </summary>
    WH_FOREGROUNDIDLE = 11,

    /// <summary>
    /// Installs a hook procedure that monitors messages posted to a message queue.
    /// For more information, see the GetMsgProc hook procedure.
    /// </summary>
    WH_GETMESSAGE = 3,
    // Not supporting WH_JOURNALPLAYBACK or WH_JOURNALRECORD

    /// <summary>
    /// Installs a hook procedure that monitors low-level keyboard input events.
    /// For more information, see the LowLevelKeyboardProc hook procedure.
    /// </summary>
    WH_KEYBOARD_LL = 13,

    /// <summary>
    /// Installs a hook procedure that monitors keystroke messages.
    /// For more information, see the KeyboardProc hook procedure.
    /// </summary>
    WH_KEYBOARD = 2,

    /// <summary>
    /// Installs a hook procedure that monitors low-level mouse input events.
    /// For more information, see the LowLevelMouseProc hook procedure. 
    /// </summary>
    WH_MOUSE_LL = 14,

    /// <summary>
    /// Installs a hook procedure that monitors messages generated as a result of an input event in a dialog box, message box, menu, or scroll bar.
    /// For more information, see the MessageProc hook procedure.
    /// </summary>
    WH_MOUSE = 7,

    /// <summary>
    /// Installs a hook procedure that monitors messages generated as a result of an input event in a dialog box, message box, menu, or scroll bar.
    /// For more information, see the MessageProc hook procedure.
    /// </summary>
    WH_MSGFILTER = -1,

    /// <summary>
    /// Installs a hook procedure that monitors messages generated as a result of an input event in a dialog box,
    /// message box, menu, or scroll bar.
    /// The hook procedure monitors these messages for all applications in the same desktop as the calling thread.
    /// For more information, see the SysMsgProc hook procedure. 
    /// </summary>
    WH_SYSMSGFILTER = 6,

    /// <summary>
    /// Installs a hook procedure that receives notifications useful to shell applications.
    /// For more information, see the ShellProc hook procedure.
    /// </summary>
    WH_SHELL = 10,
}
