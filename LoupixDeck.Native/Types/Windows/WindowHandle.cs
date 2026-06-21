namespace LoupixDeck.Native.Types.Windows;

public readonly struct WindowHandle(IntPtr value)
{
    private readonly IntPtr value = value;

    public static implicit operator WindowHandle(IntPtr handle) => new(handle);
    public static implicit operator IntPtr(WindowHandle handle) => handle.value;
}