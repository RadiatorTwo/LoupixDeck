using System.Collections.ObjectModel;
using System.Text;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Keeps a companion's profile and workspace structure in line with its master's. A companion's
/// <see cref="LoupedeckConfig.Profiles"/> hold mirrors: the master's profiles and workspaces with the
/// same ids, names, home workspaces and order, but the companion's own pages and LED buttons. Its own
/// profiles wait in <see cref="LoupedeckConfig.CompanionLink"/> and come back when it leaves.
///
/// Pure config manipulation, no I/O and no device calls. Every method is idempotent and reports
/// whether it changed anything, so callers only save and repaint when needed. Collections are edited
/// in place so bindings on the live config survive.
/// </summary>
public static class CompanionStructureSync
{
    /// <summary>
    /// Brings <paramref name="companion"/> into the state its role asks for: following
    /// <paramref name="masterKey"/> (built from <paramref name="master"/>), or standalone when
    /// <paramref name="masterKey"/> is null. Joining, leaving, switching masters and reconciling an
    /// existing link all go through here.
    /// </summary>
    /// <param name="master">The master's config, or null when it cannot be read. A link is then kept
    /// as it is instead of being rebuilt from nothing.</param>
    public static bool Apply(LoupedeckConfig companion, string masterKey, LoupedeckConfig master)
    {
        if (companion == null) return false;

        string linkedTo = companion.CompanionLink?.MasterDeviceKey;
        bool changed = false;

        if (linkedTo != null && !KeyEquals(linkedTo, masterKey))
            changed |= Unlink(companion);

        if (masterKey == null || master?.Profiles == null)
            return changed;

        if (companion.CompanionLink == null)
            changed |= Link(companion, masterKey);

        changed |= Reconcile(companion, master);
        return changed;
    }

    /// <summary>
    /// Like <see cref="Apply"/>, and re-attaches the device geometry afterwards: profiles that were
    /// parked or put aside were not part of <see cref="LoupedeckConfig.Profiles"/> when the config
    /// was loaded, so their pages do not know the panel size yet.
    /// </summary>
    public static bool ApplyAndAttachGeometry(LoupedeckConfig companion, string masterKey, LoupedeckConfig master)
    {
        if (!Apply(companion, masterKey, master)) return false;
        companion.ApplyDeviceGeometry(companion.Geometry);
        return true;
    }

    /// <summary>Moves the companion's own profiles aside and starts from the mirrors it had for this
    /// master before, if any.</summary>
    private static bool Link(LoupedeckConfig companion, string masterKey)
    {
        companion.CompanionLink = new CompanionLinkState
        {
            MasterDeviceKey = masterKey,
            OwnProfiles = [.. companion.Profiles],
            OwnActiveProfileId = companion.ActiveProfileId,
            OwnStartupProfileId = companion.StartupProfileId
        };

        // Mirrors parked for another master stay parked until the device leaves a group again.
        List<Profile> mirrors = [];
        if (KeyEquals(companion.ParkedCompanionProfiles?.MasterDeviceKey, masterKey))
        {
            mirrors = companion.ParkedCompanionProfiles.Profiles ?? [];
            companion.ParkedCompanionProfiles = null;
        }

        ReplaceAll(companion.Profiles, mirrors);
        return true;
    }

    /// <summary>Parks the mirrors and restores the companion's own profiles and ids.</summary>
    private static bool Unlink(LoupedeckConfig companion)
    {
        CompanionLinkState link = companion.CompanionLink;
        if (link == null) return false;

        companion.ParkedCompanionProfiles = new ParkedCompanionProfiles
        {
            MasterDeviceKey = link.MasterDeviceKey,
            Profiles = [.. companion.Profiles]
        };
        companion.CompanionLink = null;

        ReplaceAll(companion.Profiles, link.OwnProfiles ?? []);
        companion.StartupProfileId = link.OwnStartupProfileId;

        // A device that had no profile of its own when it joined gets the usual Default profile.
        if (companion.Profiles.Count == 0)
            companion.EnsureDefaultProfile();
        else if (companion.Profiles.Any(p => p.Id == link.OwnActiveProfileId))
            companion.ActiveProfileId = link.OwnActiveProfileId;
        return true;
    }

    /// <summary>
    /// Matches the companion's mirrors to the master's profiles and workspaces: adds what is missing,
    /// drops what the master no longer has (with the companion's pages in it), and copies names, home
    /// workspaces, order and the startup profile.
    /// </summary>
    private static bool Reconcile(LoupedeckConfig companion, LoupedeckConfig master)
    {
        if (string.Equals(StructureFingerprint(companion), StructureFingerprint(master), StringComparison.Ordinal))
            return false;

        ObservableCollection<Profile> mirrors = companion.Profiles;
        List<Profile> source = [.. master.Profiles];

        for (int i = mirrors.Count - 1; i >= 0; i--)
            if (source.All(p => p.Id != mirrors[i].Id))
                mirrors.RemoveAt(i);

        for (int i = 0; i < source.Count; i++)
        {
            Profile wanted = source[i];
            int at = IndexOf(mirrors, wanted.Id);
            Profile mirror;
            if (at < 0)
            {
                mirror = new Profile { Id = wanted.Id };
                mirrors.Insert(i, mirror);
            }
            else
            {
                mirror = mirrors[at];
                if (at != i) mirrors.Move(at, i);
            }

            if (!string.Equals(mirror.Name, wanted.Name, StringComparison.Ordinal))
                mirror.Name = wanted.Name;
            mirror.HomeWorkspaceId = wanted.HomeWorkspaceId;
            ReconcileWorkspaces(mirror.Workspaces, wanted.Workspaces);
        }

        companion.StartupProfileId = master.StartupProfileId;
        return true;
    }

    private static void ReconcileWorkspaces(ObservableCollection<Workspace> mirrors, IEnumerable<Workspace> wanted)
    {
        List<Workspace> source = [.. wanted ?? []];

        for (int i = mirrors.Count - 1; i >= 0; i--)
            if (source.All(w => w.Id != mirrors[i].Id))
                mirrors.RemoveAt(i);

        for (int i = 0; i < source.Count; i++)
        {
            int at = IndexOf(mirrors, source[i].Id);
            Workspace mirror;
            if (at < 0)
            {
                // Starts without pages; activating it creates the first, empty one.
                mirror = new Workspace { Id = source[i].Id };
                mirrors.Insert(i, mirror);
            }
            else
            {
                mirror = mirrors[at];
                if (at != i) mirrors.Move(at, i);
            }

            if (!string.Equals(mirror.Name, source[i].Name, StringComparison.Ordinal))
                mirror.Name = source[i].Name;
        }
    }

    /// <summary>The structure the mirrors copy: ids, names, home workspaces, order, startup profile.</summary>
    public static string StructureFingerprint(LoupedeckConfig config)
    {
        StringBuilder text = new();
        text.Append(config.StartupProfileId).Append('|');
        foreach (Profile profile in config.Profiles ?? [])
        {
            text.Append("P:").Append(profile.Id).Append(':').Append(profile.Name)
                .Append(':').Append(profile.HomeWorkspaceId).Append(';');
            foreach (Workspace workspace in profile.Workspaces ?? [])
                text.Append("W:").Append(workspace.Id).Append(':').Append(workspace.Name).Append(';');
        }
        return text.ToString();
    }

    private static void ReplaceAll(ObservableCollection<Profile> target, IEnumerable<Profile> items)
    {
        List<Profile> list = [.. items];
        target.Clear();
        foreach (Profile profile in list)
            target.Add(profile);
    }

    private static int IndexOf(IList<Profile> profiles, Guid id)
    {
        for (int i = 0; i < profiles.Count; i++)
            if (profiles[i].Id == id) return i;
        return -1;
    }

    private static int IndexOf(IList<Workspace> workspaces, Guid id)
    {
        for (int i = 0; i < workspaces.Count; i++)
            if (workspaces[i].Id == id) return i;
        return -1;
    }

    private static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
