using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.AppActivation;
using Tmds.DBus;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// On GNOME Wayland, "Single Instance" of Launch Application can only bring a native Wayland
/// window forward through the "Activate Window By Title" Shell extension (see
/// <see cref="GnomeActivationExtension"/>). This check reports whether it is usable and offers
/// the matching repair: installing it through GNOME's own dialog, enabling it, or switching user
/// extensions back on. None of these needs root rights, so they run from the button.
/// </summary>
public sealed class GnomeActivationCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.gnome-activation";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public async Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        if (!GnomeActivationExtension.IsGnome() || LinuxSystemFacts.SessionType() != "wayland")
        {
            return DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_GnomeActivationNotApplicable"));
        }

        GnomeExtensionState state;
        string raw;

        try
        {
            (state, raw) = await GnomeActivationExtension.QueryAsync(cancellationToken);
        }
        catch (DBusException ex)
        {
            return DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_GnomeActivationShellUnavailable"), ex.Message);
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["extension"] = GnomeActivationExtension.Uuid,
            ["state"] = state.ToString().ToLowerInvariant()
        };

        if (raw != null)
        {
            evidence["shell_state"] = raw;
        }

        switch (state)
        {
            case GnomeExtensionState.Active:
                return DiagnosticCheckResult.Pass(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationActive"), null, evidence,
                    Loc.Tr("Diagnostics_ValueAvailable"));

            case GnomeExtensionState.NotInstalled:
                return DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationMissing"), null,
                    new DiagnosticFix(FixKind.Command,
                        Loc.Tr("Diagnostics_FixInstallGnomeActivationFmt", GnomeActivationExtension.PageUrl),
                        GnomeActivationExtension.InstallCommand,
                        Action: new DiagnosticFixAction(Loc.Tr("Diagnostics_ActionInstallExtension"),
                            async token => await GnomeActivationExtension.InstallAsync(token))),
                    evidence, Loc.Tr("Diagnostics_ValueMissing"));

            case GnomeExtensionState.Disabled:
                return DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationDisabled"), null,
                    new DiagnosticFix(FixKind.Command, Loc.Tr("Diagnostics_FixEnableGnomeActivation"),
                        GnomeActivationExtension.EnableCommand,
                        Action: new DiagnosticFixAction(Loc.Tr("Diagnostics_ActionEnableExtension"),
                            GnomeActivationExtension.EnableAsync)),
                    evidence, Loc.Tr("Diagnostics_ValueDisabled"));

            case GnomeExtensionState.UserExtensionsDisabled:
                return DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationUserExtensionsOff"), null,
                    new DiagnosticFix(FixKind.Command, Loc.Tr("Diagnostics_FixEnableUserExtensions"),
                        GnomeActivationExtension.EnableUserExtensionsCommand,
                        Action: new DiagnosticFixAction(Loc.Tr("Diagnostics_ActionEnableUserExtensions"),
                            GnomeActivationExtension.EnableUserExtensionsAsync)),
                    evidence, Loc.Tr("Diagnostics_ValueDisabled"));

            case GnomeExtensionState.Broken:
                return DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationBroken"), null,
                    new DiagnosticFix(FixKind.Manual,
                        Loc.Tr("Diagnostics_FixRepairGnomeActivationFmt", GnomeActivationExtension.PageUrl),
                        RequiresLogout: true),
                    evidence, Loc.Tr("Diagnostics_ValueFailed"));

            default:
                return DiagnosticCheckResult.Unknown(Id, Category, title,
                    Loc.Tr("Diagnostics_GnomeActivationShellUnavailable"));
        }
    }
}
