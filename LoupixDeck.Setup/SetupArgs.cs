namespace LoupixDeck.Setup;

/// <summary>The operation the setup executable was launched to perform.</summary>
public enum SetupMode
{
    /// <summary>Fresh install (or update, if an existing install is detected at runtime).</summary>
    Install,

    /// <summary>Apply the embedded (newer) payload over an existing install, with backup/rollback.</summary>
    Update,

    /// <summary>Re-extract the payload over the existing install to fix damaged files.</summary>
    Repair
}

/// <summary>
/// Parsed command line for the setup. Uninstall is handled by the separate, tiny
/// <c>LoupixDeck.Uninstall</c> tool (registered in the install dir), not by this exe.
/// </summary>
public sealed class SetupArgs
{
    public SetupMode Mode { get; init; } = SetupMode.Install;

    /// <summary>Explicit install/target directory (<c>--dir &lt;path&gt;</c>); null → resolve default/registry.</summary>
    public string? TargetDir { get; init; }

    /// <summary>Skip the wizard and run the operation headlessly (used by the in-app updater later).</summary>
    public bool Silent { get; init; }

    /// <summary>Set once the process has been relaunched elevated, to avoid an elevation loop.</summary>
    public bool Elevated { get; init; }

    public static SetupArgs Parse(string[] args)
    {
        SetupMode mode = SetupMode.Install;
        string? dir = null;
        bool silent = false;
        bool elevated = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--update" or "/update":
                    mode = SetupMode.Update;
                    break;
                case "--repair" or "/repair":
                    mode = SetupMode.Repair;
                    break;
                case "--silent" or "/silent" or "/s":
                    silent = true;
                    break;
                case "--elevated":
                    elevated = true;
                    break;
                case "--dir" or "/dir":
                    if (i + 1 < args.Length)
                        dir = args[++i];
                    break;
            }
        }

        return new SetupArgs
        {
            Mode = mode,
            TargetDir = dir,
            Silent = silent,
            Elevated = elevated
        };
    }
}
