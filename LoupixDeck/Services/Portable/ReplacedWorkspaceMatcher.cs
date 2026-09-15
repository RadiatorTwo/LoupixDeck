using LoupixDeck.Models;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Pairs the workspaces of a profile that an import replaces with the workspaces of the incoming
/// profile. Replacing gives the incoming workspaces fresh ids, so whatever pointed at an old
/// workspace (context rules, macros, a companion's mirror with its own pages) is moved to its
/// counterpart instead of being dropped.
/// </summary>
public static class ReplacedWorkspaceMatcher
{
    /// <summary>
    /// Returns, for every old workspace that has a counterpart, the index of that counterpart in
    /// <paramref name="incoming"/>. Each incoming workspace is used at most once. Tried in order:
    /// the same id (the package is an export of this very profile), the same name (unique on both
    /// sides, case-insensitive), then the same position.
    /// </summary>
    /// <param name="replaced">Workspaces of the profile being replaced.</param>
    /// <param name="incoming">Workspaces of the package, with their ids as exported (before remapping).</param>
    public static Dictionary<Guid, int> Match(IReadOnlyList<Workspace> replaced, IReadOnlyList<Workspace> incoming)
    {
        Dictionary<Guid, int> matches = [];
        if (replaced == null || incoming == null) return matches;

        HashSet<int> used = [];

        void Take(Workspace old, int index)
        {
            matches[old.Id] = index;
            used.Add(index);
        }

        foreach (Workspace old in replaced)
        {
            int index = IndexWhere(incoming, used, w => w.Id == old.Id);
            if (index >= 0) Take(old, index);
        }

        foreach (Workspace old in replaced.Where(w => !matches.ContainsKey(w.Id)))
        {
            if (string.IsNullOrWhiteSpace(old.Name) ||
                replaced.Count(w => NameEquals(w.Name, old.Name)) != 1 ||
                incoming.Count(w => NameEquals(w.Name, old.Name)) != 1)
                continue;

            int index = IndexWhere(incoming, used, w => NameEquals(w.Name, old.Name));
            if (index >= 0) Take(old, index);
        }

        for (int i = 0; i < replaced.Count && i < incoming.Count; i++)
        {
            if (!matches.ContainsKey(replaced[i].Id) && !used.Contains(i))
                Take(replaced[i], i);
        }

        return matches;
    }

    private static int IndexWhere(IReadOnlyList<Workspace> workspaces, HashSet<int> used, Func<Workspace, bool> predicate)
    {
        for (int i = 0; i < workspaces.Count; i++)
            if (!used.Contains(i) && predicate(workspaces[i])) return i;
        return -1;
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
