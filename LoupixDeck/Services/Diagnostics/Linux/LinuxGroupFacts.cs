using System.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// What is known about one system group: whether it exists, whether the running process is
/// effectively in it, and whether the user is configured to be in it.
///
/// The two last flags differ exactly in the case that confuses users most: usermod has already
/// added the account, but the session was started before that and still runs without the group.
/// </summary>
/// <param name="Exists">The group is defined on this system.</param>
/// <param name="Effective">The running process actually carries the group.</param>
/// <param name="Configured">The user is listed as a member of the group.</param>
/// <param name="GroupId">The group's numeric id, or null when it is unknown.</param>
public sealed record GroupMembership(bool Exists, bool Effective, bool Configured, uint? GroupId);

/// <summary>Resolves group membership without shelling out where /etc/group already answers.</summary>
internal static class LinuxGroupFacts
{
    /// <summary>
    /// Looks <paramref name="groupName"/> up in /etc/group, falling back to getent for systems
    /// whose groups come from LDAP or SSSD rather than from the file.
    /// </summary>
    public static GroupMembership Resolve(string groupName)
    {
        string line = FindInGroupFile(groupName) ?? FindWithGetent(groupName);

        if (line == null)
        {
            return new GroupMembership(false, false, false, null);
        }

        // name:password:gid:member,member
        string[] fields = line.Split(':');

        if ((fields.Length < 3) || !uint.TryParse(fields[2], out uint groupId))
        {
            return new GroupMembership(true, false, false, null);
        }

        string[] members = fields.Length >= 4
            ? fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        bool configured = members.Contains(Environment.UserName, StringComparer.Ordinal);
        uint[] effectiveGroups = LinuxInputInterop.EffectiveGroups();
        bool effective = (effectiveGroups != null) && effectiveGroups.Contains(groupId);

        return new GroupMembership(true, effective, configured, groupId);
    }

    private static string FindInGroupFile(string groupName)
    {
        try
        {
            foreach (string line in File.ReadLines("/etc/group"))
            {
                if (line.StartsWith(groupName + ":", StringComparison.Ordinal))
                {
                    return line;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] Could not read /etc/group: {ex.Message}");
        }

        return null;
    }

    private static string FindWithGetent(string groupName)
    {
        try
        {
            ProcessStartInfo startInfo = new("getent", ["group", groupName])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);

            if (process == null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            string line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch (Exception ex)
        {
            // getent is not guaranteed to be installed; a missing binary just means "unknown".
            Console.WriteLine($"[Diagnostics] Could not run getent: {ex.Message}");
            return null;
        }
    }
}
