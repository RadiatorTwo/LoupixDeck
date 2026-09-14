using LoupixDeck.Models.Companion;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Enforces the structural rules of the companion system on a set of groups:
/// a device is in at most one group, is either master or companion there, a group has exactly
/// one master and at least one companion, and groups never nest. Used both when a file is loaded
/// (to heal it) and by the editor (to explain why an assignment is refused).
/// </summary>
public static class CompanionGroupValidator
{
    /// <summary>
    /// Returns every rule violation in <paramref name="groups"/>, in a form suitable for the UI.
    /// An empty list means the set is valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(IEnumerable<CompanionGroup> groups)
    {
        List<string> problems = [];
        Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);

        foreach (CompanionGroup group in groups)
        {
            string label = string.IsNullOrWhiteSpace(group.Name) ? group.Id.ToString() : group.Name;

            if (string.IsNullOrWhiteSpace(group.MasterDeviceKey))
                problems.Add($"Group '{label}' has no master.");

            if (group.CompanionDeviceKeys == null || group.CompanionDeviceKeys.Count == 0)
                problems.Add($"Group '{label}' has no companion.");

            if (!string.IsNullOrWhiteSpace(group.MasterDeviceKey) &&
                (group.CompanionDeviceKeys ?? []).Contains(group.MasterDeviceKey, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"Group '{label}': '{group.MasterDeviceKey}' is both master and companion.");
            }

            foreach (string key in Members(group))
            {
                if (owners.TryGetValue(key, out string other) && !string.Equals(other, label, StringComparison.Ordinal))
                    problems.Add($"Device '{key}' is in both '{other}' and '{label}'.");
                else
                    owners[key] = label;
            }
        }

        return problems;
    }

    /// <summary>
    /// Rewrites <paramref name="groups"/> in place so no device is claimed twice, keeping as much of
    /// the user's intent as possible: duplicate memberships keep their first occurrence and a master
    /// listed among its own companions is removed from the companion list. Incomplete groups (no
    /// master yet, or no companion yet) are kept — they are being set up in the editor and assign
    /// no roles until complete. Returns true when anything changed.
    /// </summary>
    public static bool Heal(List<CompanionGroup> groups)
    {
        bool changed = false;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (CompanionGroup group in groups)
        {
            group.Name ??= string.Empty;
            group.MasterDeviceKey ??= string.Empty;
            group.CompanionDeviceKeys ??= [];

            if (!string.IsNullOrWhiteSpace(group.MasterDeviceKey) && !seen.Add(group.MasterDeviceKey))
            {
                group.MasterDeviceKey = string.Empty;
                changed = true;
            }

            List<string> companions = [];
            foreach (string key in group.CompanionDeviceKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) { changed = true; continue; }
                if (string.Equals(key, group.MasterDeviceKey, StringComparison.OrdinalIgnoreCase)) { changed = true; continue; }
                if (!seen.Add(key)) { changed = true; continue; }
                companions.Add(key);
            }

            if (companions.Count != group.CompanionDeviceKeys.Count)
                group.CompanionDeviceKeys = companions;
        }

        return changed;
    }

    /// <summary>
    /// True when the scope key carries a serial (<c>slug_serial</c>). A slug-only key names every
    /// unit of that model at once. Slugs never contain an underscore, so the separator is unambiguous.
    /// </summary>
    public static bool HasSerial(string deviceKey) =>
        !string.IsNullOrWhiteSpace(deviceKey) && deviceKey.Contains('_');

    /// <summary>
    /// True when <paramref name="deviceKey"/> names exactly one physical device among
    /// <paramref name="knownKeys"/>. A key with a serial always does. A slug-only key does as long as
    /// no other device of the same model is known — then the slug is as unique as the config file it
    /// names. Many units report no USB serial at all (Windows then only offers a port-dependent id),
    /// so refusing every slug-only key would lock them out even when they are the only one of their model.
    /// </summary>
    public static bool IsDistinguishable(string deviceKey, IEnumerable<string> knownKeys)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return false;
        if (HasSerial(deviceKey)) return true;

        return !knownKeys.Any(key =>
            !string.Equals(key, deviceKey, StringComparison.OrdinalIgnoreCase) &&
            key.StartsWith(deviceKey + "_", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>All device keys a group references: the master followed by its companions.</summary>
    public static IEnumerable<string> Members(CompanionGroup group)
    {
        if (!string.IsNullOrWhiteSpace(group.MasterDeviceKey))
            yield return group.MasterDeviceKey;

        foreach (string key in group.CompanionDeviceKeys ?? [])
            if (!string.IsNullOrWhiteSpace(key))
                yield return key;
    }
}
