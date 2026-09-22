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
        }
        catch (Exception ex)
        {
            // A missing browser or opener must not take the button's command chain down.
            Console.WriteLine($"System.OpenUrl failed for '{uri.AbsoluteUri}': {ex.Message}");
        }

        return Task.CompletedTask;
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
