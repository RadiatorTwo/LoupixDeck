using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace LoupixDeck.Setup;

internal static partial class Program
{
    /// <summary>
    /// Parsed command-line context, shared with the UI. Populated before Avalonia starts
    /// so the wizard can open directly in the right mode (install / update / repair / uninstall).
    /// </summary>
    public static SetupArgs Args { get; private set; } = new();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

    // Avalonia requires a STA thread; NativeAOT-safe entry point.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Args = SetupArgs.Parse(args);

            // Ensure Avalonia's embedded native renderer libs are on the DLL search path
            // (single-exe self-extraction) before any UI work touches Skia.
            NativeBootstrap.Prepare();

            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // WinExe + NativeAOT has no console; without this the process just vanishes.
            try
            {
                StringBuilder sb = new();
                for (Exception? e = ex; e != null; e = e.InnerException)
                {
                    sb.AppendLine(e.GetType().FullName);
                    sb.AppendLine(e.Message);
                    if (!string.IsNullOrEmpty(e.StackTrace))
                        sb.AppendLine(e.StackTrace);
                    sb.AppendLine();
                }
                MessageBoxW(0, sb.ToString(), "LoupixDeck Setup failed", 0x00000010);
            }
            catch
            {
                // last resort: nothing else we can show
            }

            return 1;
        }
    }

    // Referenced by the Avalonia previewer/build tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
