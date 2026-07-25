using LoupixDeck.Models;

namespace LoupixDeck.Services.AppSwitching;

/// <summary>
/// Pure rule-selection logic for the context engine (issue #132), split out so it can be tested
/// without the UI-thread timers of <see cref="AppSwitchingService"/>.
/// </summary>
public static class ContextRuleMatcher
{
    /// <summary>
    /// Returns the best rule for the given foreground process/title: the highest
    /// <see cref="ContextRule.Priority"/> among all matches, breaking ties by list order (the
    /// earlier rule wins), which preserves the old first-match-wins behaviour. Null when none match.
    /// </summary>
    public static ContextRule MatchBest(IEnumerable<ContextRule> rules, string process, string title)
    {
        process = Normalize(process);
        if (string.IsNullOrEmpty(process)) return null;
        title ??= string.Empty;

        ContextRule best = null;
        foreach (var rule in rules)
        {
            if (!Matches(rule, process, title)) continue;
            // Strict '>' keeps the earlier rule on a priority tie.
            if (best == null || rule.Priority > best.Priority)
                best = rule;
        }

        return best;
    }

    /// <summary>True when, for each of <see cref="ContextRule.ProcessName"/> and
    /// <see cref="ContextRule.TitleContains"/> that is set, the corresponding argument matches
    /// (process case-insensitively and ".exe"-stripped, title by substring); a rule with neither set
    /// never matches. <paramref name="process"/> is expected already normalized.
    ///
    /// Leaving ProcessName blank matters on Linux for sandboxed apps (Flatpak/Snap): their window's
    /// _NET_WM_PID is a PID inside the sandbox's own namespace, meaningless to the host's /proc
    /// lookup, so ProcessName can never resolve correctly for them — TitleContains is the only
    /// usable match key.</summary>
    public static bool Matches(ContextRule rule, string process, string title)
    {
        var ruleProcess = Normalize(rule.ProcessName);
        var hasTitleFilter = !string.IsNullOrEmpty(rule.TitleContains);

        if (string.IsNullOrEmpty(ruleProcess) && !hasTitleFilter) return false;

        if (!string.IsNullOrEmpty(ruleProcess) &&
            !string.Equals(ruleProcess, process, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (hasTitleFilter &&
            (title ?? string.Empty).IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>Strips a trailing ".exe" so Windows and Linux rules are portable.</summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
}
