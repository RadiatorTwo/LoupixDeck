using System.Runtime.InteropServices;
using LoupixDeck.Setup.Services;

namespace LoupixDeck.Uninstall;

/// <summary>
/// Tiny standalone uninstaller placed in the install directory and registered in the Windows uninstall
/// list. It carries no payload (unlike the full setup) and no Avalonia/Skia — just a native confirm
/// dialog and the shared <see cref="UninstallRunner"/>.
/// </summary>
internal static partial class Program
{
    private const uint MB_YESNOCANCEL = 0x00000003;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_DEFBUTTON2 = 0x00000100;
    private const int IDCANCEL = 2;
    private const int IDYES = 6;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        bool silent = false, fromTemp = false, removeUserData = false;
        string? dir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--silent" or "/silent" or "/s": silent = true; break;
                case "--from-temp": fromTemp = true; break;
                case "--remove-user-data": removeUserData = true; break;
                case "--dir" or "/dir":
                    if (i + 1 < args.Length)
                        dir = args[++i];
                    break;
            }
        }

        string installDir = dir
                            ?? UninstallRegistry.GetInstalledLocation()
                            ?? AppPaths.DefaultInstallDir();

        // Interactive confirm with the "also remove my data" choice folded into the buttons.
        if (!silent && !fromTemp)
        {
            int choice = MessageBox(IntPtr.Zero,
                "This will uninstall LoupixDeck.\n\n" +
                "Do you also want to remove your configuration files " +
                "(settings, layouts, plugins and logs)?\n\n" +
                "Yes — remove them\n" +
                "No — keep them\n" +
                "Cancel — don't uninstall",
                "Uninstall LoupixDeck",
                MB_YESNOCANCEL | MB_ICONWARNING | MB_DEFBUTTON2);

            if (choice == IDCANCEL)
                return 0;

            removeUserData = choice == IDYES;
        }

        UninstallOutcome outcome = UninstallRunner.Run(installDir, removeUserData, fromTemp);

        // The temp copy finishes the job silently; nothing more to show here.
        if (outcome.HandedOff)
            return 0;

        if (!silent && !fromTemp)
        {
            MessageBox(IntPtr.Zero, outcome.Message, "Uninstall LoupixDeck",
                outcome.Success ? MB_ICONINFORMATION : MB_ICONERROR);
        }

        return outcome.Success ? 0 : 1;
    }
}
