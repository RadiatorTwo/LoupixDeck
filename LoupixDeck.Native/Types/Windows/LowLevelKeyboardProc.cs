namespace LoupixDeck.Native.Types.Windows;

public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
public delegate void LowLevelKeyboardProcHandler(int nCode, IntPtr wParam, IntPtr lParam);