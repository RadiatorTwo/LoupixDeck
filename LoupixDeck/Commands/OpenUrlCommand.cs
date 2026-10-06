using System.Diagnostics;
using LoupixDeck.Commands.Base;
using LoupixDeck.Utils;

namespace LoupixDeck.Commands;

/// <summary>
/// Opens a web address in the default browser. Only http and https are opened, so a hand-edited
/// button can never turn this into a generic launcher — that is what <c>System.LaunchApp</c> is for.
/// </summary>
/// <remarks>
/// The address is stored escaped by <see cref="CommandParameterEncoding"/>, because URLs routinely
/// contain the <c>,</c> and <c>)</c> that <see cref="CommandStringParser"/> splits on. It is opened
/// with the platform's default handler, never through a shell, so no quoting rules apply.
/// </remarks>
[Command(
    OpenUrlCommand.CommandName,
    "Open Website",
    "Shell",
    "({Url})",
    ["Url"],
    [typeof(string)],
    Platform = CommandPlatform.All,
    Icon = "\U000F059F", // mdi-web
    Description = "Open a web address in the default browser")]
public sealed class OpenUrlCommand : IExecutableCommand
{
    public const string CommandName = "System.OpenUrl";

    public Task Execute(string[] parameters)
    {
        if (parameters.Length == 0)
        {
            Console.WriteLine("Usage: System.OpenUrl(url)");
            return Task.CompletedTask;
        }

        // GetParameters splits on ','; an encoded address has none, a hand-typed one may.
        string raw = CommandParameterEncoding.Decode(string.Join(",", parameters)).Trim();
        if (!TryNormalize(raw, out Uri uri))
        {
            Console.WriteLine($"System.OpenUrl: refused '{raw}' (only http and https addresses are opened)");
            return Task.CompletedTask;
        }

        Launch(uri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens <paramref name="text"/> in the default browser when it is an http or https address
    /// (see <see cref="TryNormalize"/>). Never throws; false when the address was refused or the
    /// opener could not be started.
    /// </summary>
    public static bool TryOpen(string text)
    {
        if (!TryNormalize(text, out Uri uri))
        {
            Console.WriteLine($"[OpenUrl] Refused '{text}' (only http and https addresses are opened)");
            return false;
        }

        return Launch(uri);
    }

    private static bool Launch(Uri uri)
    {
        try
        {
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
            }
            else
            {
                // ArgumentList, not the Arguments string: that one is re-split on whitespace, so an
                // address carrying an encoded space would reach the opener as several arguments.
                start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                {
                    UseShellExecute = false
                };
                start.ArgumentList.Add(uri.AbsoluteUri);
            }

            using Process process = Process.Start(start);
            return true;
        }
        catch (Exception ex)
        {
            // A missing browser or opener must not take the caller down.
            Console.WriteLine($"[OpenUrl] Could not open '{uri.AbsoluteUri}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// An absolute http or https address for <paramref name="text"/>. A value without a scheme is
    /// taken as https. False for blank text, a missing host, and every other scheme.
    /// </summary>
    public static bool TryNormalize(string text, out Uri uri)
    {
        uri = null;
        string value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        string candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;

        return Uri.TryCreate(candidate, UriKind.Absolute, out uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host);
    }
}
