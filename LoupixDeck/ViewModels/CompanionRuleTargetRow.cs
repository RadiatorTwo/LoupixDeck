using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.ViewModels;

/// <summary>One page choice in a companion page ComboBox. A null <see cref="Id"/> leaves the page unchanged.</summary>
public sealed record RulePageOption(Guid? Id, string Label);

/// <summary>
/// One companion inside a profile rule's editor card: the pages the rule opens on it, chosen from the
/// companion's mirror of the rule's workspace. Selections write straight into the rule's
/// <see cref="ContextRule.CompanionPageTargets"/>; a companion with no page chosen has no target, and a
/// rule without targets stores none. A target of a device that left the group is shown as stale so it
/// can be removed.
/// </summary>
public sealed partial class CompanionRuleTargetRow : ObservableObject
{
    private readonly ContextRule _rule;
    private readonly Action<CompanionRuleTargetRow> _remove;

    /// <param name="workspace">The companion's mirror of the rule's workspace; null when the rule names
    /// no workspace, the companion's config cannot be read, or the device is stale.</param>
    public CompanionRuleTargetRow(ContextRule rule, string deviceKey, string displayName, bool isOnline,
        bool isInGroup, Workspace workspace, bool hasSideStrips, Action<CompanionRuleTargetRow> remove)
    {
        _rule = rule;
        _remove = remove;
        DeviceKey = deviceKey;
        DeviceLabel = !isInGroup || isOnline ? displayName : Loc.Tr("CompanionMenu_OfflineDeviceFmt", displayName);
        IsInGroup = isInGroup;
        HasWorkspace = workspace != null;
        HasSideStrips = hasSideStrips;

        TouchPages = Options(workspace?.TouchButtonPages, "CompanionMenu_TouchPageFmt");
        RotaryPages = Options(workspace?.RotaryButtonPages, "CompanionMenu_RotaryPageFmt");
        LeftRotaryPages = Options(workspace?.LeftRotaryButtonPages, "CompanionMenu_LeftRotaryPageFmt");
        RightRotaryPages = Options(workspace?.RightRotaryButtonPages, "CompanionMenu_RightRotaryPageFmt");
    }

    public string DeviceKey { get; }
    public string DeviceLabel { get; }

    public bool IsInGroup { get; }
    public bool IsStale => !IsInGroup;

    /// <summary>True when the rule's workspace is known and exists on the companion.</summary>
    public bool HasWorkspace { get; }

    /// <summary>True when the rule opens a known workspace, so a missing mirror is worth mentioning.</summary>
    public bool RuleHasWorkspace { get; init; }

    public bool CanPick => IsInGroup && HasWorkspace;

    /// <summary>The companion has no mirror of the rule's workspace yet (or its config is unreadable).</summary>
    public bool ShowMissingWorkspace => IsInGroup && RuleHasWorkspace && !HasWorkspace;
    public bool HasSideStrips { get; }
    public bool ShowRotary => CanPick && !HasSideStrips;
    public bool ShowSideRotary => CanPick && HasSideStrips;

    public IReadOnlyList<RulePageOption> TouchPages { get; }
    public IReadOnlyList<RulePageOption> RotaryPages { get; }
    public IReadOnlyList<RulePageOption> LeftRotaryPages { get; }
    public IReadOnlyList<RulePageOption> RightRotaryPages { get; }

    public RulePageOption SelectedTouchPage
    {
        get => Select(TouchPages, FindTarget()?.TouchPageId);
        set => Write(t => t.TouchPageId = value?.Id);
    }

    public RulePageOption SelectedRotaryPage
    {
        get => Select(RotaryPages, FindTarget()?.RotaryPageId);
        set => Write(t => t.RotaryPageId = value?.Id);
    }

    public RulePageOption SelectedLeftRotaryPage
    {
        get => Select(LeftRotaryPages, FindTarget()?.LeftRotaryPageId);
        set => Write(t => t.LeftRotaryPageId = value?.Id);
    }

    public RulePageOption SelectedRightRotaryPage
    {
        get => Select(RightRotaryPages, FindTarget()?.RightRotaryPageId);
        set => Write(t => t.RightRotaryPageId = value?.Id);
    }

    [RelayCommand]
    private void Remove() => _remove?.Invoke(this);

    /// <summary>Drops page ids the companion's workspace does not have, after the rule's workspace
    /// changed. Only valid when the workspace could be read; stale rows keep their target.</summary>
    public void PruneToWorkspace()
    {
        if (!IsInGroup || FindTarget() is not { } target) return;

        target.TouchPageId = Keep(TouchPages, target.TouchPageId);
        target.RotaryPageId = Keep(RotaryPages, target.RotaryPageId);
        target.LeftRotaryPageId = Keep(LeftRotaryPages, target.LeftRotaryPageId);
        target.RightRotaryPageId = Keep(RightRotaryPages, target.RightRotaryPageId);
        DropIfEmpty(target);
    }

    /// <summary>Removes this companion's target from the rule.</summary>
    public void RemoveTarget()
    {
        if (FindTarget() is { } target)
        {
            target.TouchPageId = target.RotaryPageId = target.LeftRotaryPageId = target.RightRotaryPageId = null;
            DropIfEmpty(target);
        }
    }

    private CompanionPageTarget FindTarget() =>
        _rule.CompanionPageTargets?.FirstOrDefault(t =>
            string.Equals(t.DeviceKey, DeviceKey, StringComparison.OrdinalIgnoreCase));

    private void Write(Action<CompanionPageTarget> apply, [CallerMemberName] string propertyName = null)
    {
        CompanionPageTarget target = FindTarget();
        if (target == null)
        {
            target = new CompanionPageTarget { DeviceKey = DeviceKey };
            List<CompanionPageTarget> targets = _rule.CompanionPageTargets ?? [];
            targets.Add(target);
            _rule.CompanionPageTargets = targets;
        }

        apply(target);
        DropIfEmpty(target);
        OnPropertyChanged(propertyName);
    }

    private void DropIfEmpty(CompanionPageTarget target)
    {
        if (target.TouchPageId != null || target.RotaryPageId != null ||
            target.LeftRotaryPageId != null || target.RightRotaryPageId != null)
            return;

        _rule.CompanionPageTargets?.Remove(target);
        if (_rule.CompanionPageTargets is { Count: 0 })
            _rule.CompanionPageTargets = null;
    }

    private static RulePageOption Select(IReadOnlyList<RulePageOption> options, Guid? id) =>
        options.FirstOrDefault(o => o.Id == id) ?? options[0];

    private static Guid? Keep(IReadOnlyList<RulePageOption> options, Guid? id) =>
        id != null && options.Any(o => o.Id == id) ? id : null;

    private static IReadOnlyList<RulePageOption> Options<TPage>(IList<TPage> pages, string labelKey)
        where TPage : ButtonPageBase
    {
        List<RulePageOption> options = [new RulePageOption(null, Loc.Tr("Settings_RuleCompanionUnchanged"))];
        if (pages != null)
        {
            for (int i = 0; i < pages.Count; i++)
                options.Add(new RulePageOption(pages[i].Id, CompanionDeviceTraits.PageLabel(labelKey, i, pages[i].Name)));
        }
        return options;
    }
}
