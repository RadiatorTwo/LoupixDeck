using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LoupixDeck.Setup.Services;
using LoupixDeck.Setup.ViewModels;
using LoupixDeck.Setup.Views;

namespace LoupixDeck.Setup;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.Args.Silent)
            {
                // Headless operation (temp-copy uninstall finish; future in-app --update --silent).
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                _ = RunSilentAsync(desktop);
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new WizardViewModel(Program.Args)
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunSilentAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        InstallService service = new();
        SetupArgs args = Program.Args;
        string installDir = args.TargetDir
                            ?? UninstallRegistry.GetInstalledLocation()
                            ?? AppPaths.DefaultInstallDir();

        OpResult result;
        try
        {
            result = args.Mode switch
            {
                SetupMode.Update => await service.UpdateAsync(installDir, restartAfter: true, null),
                SetupMode.Repair => await service.RepairAsync(installDir, RepairPlan.Full, restartAfter: false, null),
                _ => await service.InstallAsync(new InstallOptions { InstallDir = installDir }, null)
            };
        }
        catch
        {
            result = OpResult.Fail("Silent operation failed.");
        }

        desktop.Shutdown(result.Success ? 0 : 1);
    }
}
