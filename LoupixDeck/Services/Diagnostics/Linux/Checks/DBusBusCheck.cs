using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using Tmds.DBus;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Shared body of the two bus checks. It connects and nothing else: no method call, no name
/// registration, so the probe stays read-only and cheap.
/// </summary>
public abstract class DBusBusCheck : ILinuxDiagnosticCheck
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    public abstract string Id { get; }

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    /// <summary>The bus address to connect to.</summary>
    protected abstract string Address { get; }

    /// <summary>
    /// True when a bus that cannot be reached is a failure. The session bus carries the desktop
    /// integrations, so it fails; nothing in phase 1 uses the system bus, so it warns.
    /// </summary>
    protected abstract bool UnavailableIsFailure { get; }

    public async Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string address;

        try
        {
            address = Address;
        }
        catch (Exception ex)
        {
            return Unavailable(title, Loc.Tr("Diagnostics_DBusNoAddress"), ex.Message);
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return Unavailable(title, Loc.Tr("Diagnostics_DBusNoAddress"));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["address"] = address
        };

        Connection connection = null;

        try
        {
            connection = new Connection(address);
            await connection.ConnectAsync().WaitAsync(ConnectTimeout, cancellationToken);

            return DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_DBusConnected"), null, evidence);
        }
        catch (TimeoutException)
        {
            return DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DBusTimedOut"), address);
        }
        catch (Exception ex)
        {
            return Unavailable(title, Loc.Tr("Diagnostics_DBusUnavailable"),
                $"{ex.GetType().Name}: {ex.Message}", evidence);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private DiagnosticCheckResult Unavailable(string title, string summary,
        string technicalDetail = null, IReadOnlyDictionary<string, string> evidence = null)
    {
        if (UnavailableIsFailure)
        {
            return DiagnosticCheckResult.Fail(Id, Category, title, summary, technicalDetail, null, evidence);
        }

        return DiagnosticCheckResult.Warning(Id, Category, title, summary, technicalDetail, null, evidence);
    }
}

/// <summary>The session bus, which carries notifications and the desktop integrations.</summary>
public sealed class DBusSessionBusCheck : DBusBusCheck
{
    public override string Id => "session.dbus-session";

    protected override string Address => Tmds.DBus.Address.Session;

    protected override bool UnavailableIsFailure => true;
}

/// <summary>The system bus. Nothing in phase 1 depends on it, so its absence only warns.</summary>
public sealed class DBusSystemBusCheck : DBusBusCheck
{
    public override string Id => "session.dbus-system";

    protected override string Address => Tmds.DBus.Address.System;

    protected override bool UnavailableIsFailure => false;
}
