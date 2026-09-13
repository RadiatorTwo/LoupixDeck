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
    /// activates the profile or when the matching rules name no process.
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
    /// Rules that match the same process but do not activate <paramref name="profileId"/>. Only one
    /// rule is applied per window, so any of these would shadow a link to this profile.
    /// </summary>
    public static IReadOnlyList<ContextRule> FindConflictingRules(IEnumerable<ContextRule> rules, Guid profileId,
        string processName)
    {
        string process = ContextRuleMatcher.Normalize(processName);
        if (process.Length == 0)
            return [];

        return rules
            .Where(rule => rule.ActivateProfileId != profileId
                           && string.Equals(ContextRuleMatcher.Normalize(rule.ProcessName), process,
                               StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Replaces the profile's app rules with a single rule for <paramref name="processName"/>.
    /// Rules that match only by window title are kept.</summary>
    public static void Link(IList<ContextRule> rules, Guid profileId, string processName)
    {
        Unlink(rules, profileId);
        rules.Add(new ContextRule
        {
            ProcessName = ContextRuleMatcher.Normalize(processName),
            ActivateProfileId = profileId
        });
    }

    /// <summary>Removes every rule that activates the profile by process name.</summary>
    public static void Unlink(IList<ContextRule> rules, Guid profileId)
    {
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            if (IsAppRuleFor(rules[i], profileId))
                rules.RemoveAt(i);
        }
    }

    private static bool IsAppRuleFor(ContextRule rule, Guid profileId) =>
        rule.ActivateProfileId == profileId
        && ContextRuleMatcher.Normalize(rule.ProcessName).Length > 0;
}
