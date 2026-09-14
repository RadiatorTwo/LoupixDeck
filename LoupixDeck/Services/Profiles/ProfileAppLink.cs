using LoupixDeck.Models;
using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.Profiles;

/// <summary>
/// The link between a profile and an application. It is not stored on the profile: it is the
/// process name of the context rule that activates the profile, so what the UI shows as linked can
/// never disagree with the rule that actually switches to it.
/// </summary>
public static class ProfileAppLink
{
    /// <summary>
    /// Normalized process name of the highest-priority rule that activates <paramref name="profileId"/>,
    /// breaking ties by list order as <see cref="ContextRuleMatcher"/> does. Empty when no rule
    /// activates the profile or when the matching rules name no process. This is the broad sense of
    /// "linked" used by callers such as plan 19's profile picker; it includes detailed rules (title
    /// match, a specific workspace/page, "on process start"). The header menu instead uses
    /// <see cref="FindLinkedProcessName"/>, which only recognizes the plain rule shape it itself
    /// creates, so it never claims ownership of — or silently deletes — a rule built by hand.
    /// </summary>
    public static string FindProcessName(IEnumerable<ContextRule> rules, Guid profileId)
    {
        ContextRule best = null;

        foreach (ContextRule rule in rules)
        {
            if (!IsAppRuleFor(rule, profileId))
                continue;

            // Strict '>' keeps the earlier rule on a priority tie.
            if (best == null || rule.Priority > best.Priority)
                best = rule;
        }

        return best == null ? string.Empty : ContextRuleMatcher.Normalize(best.ProcessName);
    }

    /// <summary>
    /// Rules that match the same process but do not activate <paramref name="profileId"/>, excluding
    /// rules that also filter by <see cref="ContextRule.TitleContains"/>: those only win when the
    /// title matches too, so they do not unconditionally shadow a plain link to this profile and must
    /// not be deleted as if they did. Only a plain (process-only) rule is guaranteed to shadow.
    /// </summary>
    public static IReadOnlyList<ContextRule> FindConflictingRules(IEnumerable<ContextRule> rules, Guid profileId,
        string processName)
    {
        string process = ContextRuleMatcher.Normalize(processName);
        if (process.Length == 0)
            return [];

        return rules
            .Where(rule => rule.ActivateProfileId != profileId
                           && string.IsNullOrEmpty(rule.TitleContains)
                           && string.Equals(ContextRuleMatcher.Normalize(rule.ProcessName), process,
                               StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Normalized process name of the highest-priority "plain" rule that activates
    /// <paramref name="profileId"/> — the shape the header link creates and edits (see
    /// <see cref="IsPlainAppRuleFor"/>) — breaking ties by list order as <see cref="ContextRuleMatcher"/>
    /// does. Empty when no plain rule activates the profile. Unlike <see cref="FindProcessName"/>, a
    /// detailed rule (title match, a specific workspace/page, or "on process start") is not reported
    /// as a link, so the header never claims to own, and never silently deletes, a rule it didn't create.
    /// </summary>
    public static string FindLinkedProcessName(IEnumerable<ContextRule> rules, Guid profileId)
    {
        ContextRule best = null;

        foreach (ContextRule rule in rules)
        {
            if (!IsPlainAppRuleFor(rule, profileId))
                continue;

            // Strict '>' keeps the earlier rule on a priority tie.
            if (best == null || rule.Priority > best.Priority)
                best = rule;
        }

        return best == null ? string.Empty : ContextRuleMatcher.Normalize(best.ProcessName);
    }

    /// <summary>Process names that are never the application's own foreground process, so a rule for
    /// them would either never fire or collide with every other app sharing the same wrapper:
    /// <c>flatpak</c>/<c>snap</c> are sandbox launchers that report their own process instead of the
    /// contained application's; <c>env</c>/<c>sh</c>/<c>bash</c> are shell wrappers a desktop entry's
    /// <c>Exec=</c> line runs through on Linux; <c>update</c> is Squirrel's <c>Update.exe</c>, which
    /// several Windows apps (e.g. Discord, Slack) resolve to instead of their real executable.</summary>
    private static readonly HashSet<string> UnlinkableProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "flatpak", "snap", "env", "sh", "bash", "update"
    };

    /// <summary>True when <paramref name="processName"/> names a real, distinguishable foreground
    /// process a rule can target — see <see cref="UnlinkableProcessNames"/> for what is excluded.</summary>
    public static bool CanLinkProcess(string processName)
    {
        string process = ContextRuleMatcher.Normalize(processName);
        return process.Length > 0 && !UnlinkableProcessNames.Contains(process);
    }

    /// <summary>Replaces the profile's plain app rules (see <see cref="IsPlainAppRuleFor"/>) with a
    /// single rule for <paramref name="processName"/>. Detailed rules for the profile — a title match,
    /// a specific workspace/page, or "on process start" — are left alone; only Settings edits those.</summary>
    public static void Link(IList<ContextRule> rules, Guid profileId, string processName)
    {
        Unlink(rules, profileId);
        rules.Add(new ContextRule
        {
            ProcessName = ContextRuleMatcher.Normalize(processName),
            ActivateProfileId = profileId
        });
    }

    /// <summary>Removes the profile's plain app rules (see <see cref="IsPlainAppRuleFor"/>). Detailed
    /// rules for the profile are left alone.</summary>
    public static void Unlink(IList<ContextRule> rules, Guid profileId)
    {
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            if (IsPlainAppRuleFor(rules[i], profileId))
                rules.RemoveAt(i);
        }
    }

    private static bool IsAppRuleFor(ContextRule rule, Guid profileId) =>
        rule.ActivateProfileId == profileId
        && ContextRuleMatcher.Normalize(rule.ProcessName).Length > 0;

    /// <summary>A rule the header link itself could have created: activates only the profile (no
    /// specific workspace or page, no "on process start"), matches only by process name (no title).
    /// This is the subset <see cref="Link"/> and <see cref="Unlink"/> touch, so linking from the header
    /// never silently discards a rule a person built by hand in Settings.</summary>
    private static bool IsPlainAppRuleFor(ContextRule rule, Guid profileId) =>
        IsAppRuleFor(rule, profileId)
        && string.IsNullOrEmpty(rule.TitleContains)
        && rule.ActivateWorkspaceId == null
        && rule.TouchPageIndex == null
        && rule.RotaryPageIndex == null
        && rule.TouchPageId == null
        && rule.RotaryPageId == null
        && rule.LeftRotaryPageId == null
        && rule.RightRotaryPageId == null
        && !rule.ActivateOnProcessStart;
}
