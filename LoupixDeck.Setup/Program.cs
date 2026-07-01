using Avalonia;

namespace LoupixDeck.Setup;

internal static class Program
{
    /// <summary>
    /// Parsed command-line context, shared with the UI. Populated before Avalonia starts
    /// so the wizard can open directly in the right mode (install / update / repair / uninstall).
    /// </summary>
    public static SetupArgs Args { get; private set; } = new();

    // Avalonia requires a STA thread; NativeAOT-safe entry point.
    [STAThread]
    public static int Main(string[] args)
    {
        Args = SetupArgs.Parse(args);

        // Ensure Avalonia's embedded native renderer libs are on the DLL search path
        // (single-exe self-extraction) before any UI work touches Skia.
        NativeBootstrap.Prepare();

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia previewer/build tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
