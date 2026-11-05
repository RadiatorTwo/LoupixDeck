using LoupixDeck.Localization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.CommandPicker;

/// <summary>
/// Reusable view-model for the master-detail command picker (issue #171). It projects
/// the host's <c>SystemCommandMenus</c> (<see cref="MenuEntry"/> group→leaf tree,
/// filled by <c>IMenuTreeBuilder.BuildInto</c>) into a sectioned group rail plus the
/// searchable command list of the selected group. Categories can nest arbitrarily deep
/// (e.g. Audio's <c>device → commands</c>); the detail list shows those sub-groups as
/// inline accordion sections. Selection and search live here; command insertion stays with
/// the host (the View raises activation/drag events carrying the selected leaf
/// <see cref="MenuEntry"/>).
/// </summary>
public partial class CommandPickerViewModel : ViewModelBase
{
    /// <summary>mdi-puzzle — fallback card icon for a group without declared metadata.</summary>
    private const string PluginFallbackIcon = "\U000F0431";

    // Section presentation, in fixed display order.
    private static readonly (CommandGroupSection Section, string Title, string Icon)[] SectionDefs =
    {
        (CommandGroupSection.Core, "Core", "\U000F0493"),    // mdi-cog
        (CommandGroupSection.Macros, "Macros", "\U000F0241"), // mdi-flash
        (CommandGroupSection.Plugins, "Plugins", "\U000F0431") // mdi-puzzle
    };

    private readonly ObservableCollection<MenuEntry> _source;
    private readonly List<MenuEntry> _subscribedGroups = [];

    // Every insertable leaf across all categories, flattened for search (each remembers
    // the group path it lives under so results can be grouped by that path).
    private readonly List<SearchLeaf> _searchLeaves = [];

    private sealed record SearchLeaf(CommandRowViewModel Row, string GroupPath, string GroupIcon);

    // What the user picked and opened, keyed by name path rather than by entry. A full catalogue
    // refresh clears the source and builds every entry anew, so nothing survives by reference;
    // these survive it, and the empty source in between, and are put back once the entries return.
    private string _wantedCategory;
    private string _wantedCommand;
    private readonly Dictionary<string, bool> _expansion = new(StringComparer.Ordinal);

    /// <summary>Sections shown as the card grid when not searching.</summary>
    public ObservableCollection<CommandSectionViewModel> Sections { get; } = [];

    /// <summary>Grouped search results shown instead of the card grid while searching.</summary>
    public ObservableCollection<CommandSearchGroupViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCategory))]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    public partial CommandCategoryViewModel SelectedCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedCommandLabel))]
    public partial CommandRowViewModel SelectedCommand { get; set; }

    public bool HasSelectedCategory => SelectedCategory != null;

    /// <summary>Drives the footer's "Add" button: without a selection there is nothing to insert.</summary>
    public bool HasSelectedCommand => SelectedCommand != null;

    /// <summary>Footer caption naming the row the "Add" button acts on.</summary>
    public string SelectedCommandLabel => SelectedCommand?.Title ?? Loc.Tr("CommandPicker_SelectACommand");

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(IsDetailVisible));
                ApplyFilter();
            }
        }
    } = string.Empty;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>The detail area only makes sense for a picked category outside of search mode.</summary>
    public bool IsDetailVisible => (SelectedCategory != null) && !IsSearching;

    public IRelayCommand<CommandCategoryViewModel> SelectCategoryCommand { get; }
    public IRelayCommand<CommandRowViewModel> SelectCommandCommand { get; }

    public CommandPickerViewModel(ObservableCollection<MenuEntry> source)
    {
        _source = source ?? [];
        SelectCategoryCommand = new RelayCommand<CommandCategoryViewModel>(SelectCategory);
        SelectCommandCommand = new RelayCommand<CommandRowViewModel>(SelectCommand);

        _source.CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    /// <summary>Detaches from the source collection; call when the host dialog closes.</summary>
    public void Cleanup()
    {
        _source.CollectionChanged -= OnSourceChanged;
        UnsubscribeGroups();
    }

    // ── Selection ──────────────────────────────────────────────────────────

    public void SelectCategory(CommandCategoryViewModel category)
    {
        if (category == null || ReferenceEquals(category, SelectedCategory))
            return;

        _wantedCategory = category.Title;
        _wantedCommand = null;
        ApplyCategory(category);
    }

    public void SelectCommand(CommandRowViewModel command)
    {
        // Info rows carry no command — they stay unselectable so the footer's
        // "+ Add" can never insert an empty assignment.
        if (command is { IsSelectable: false })
            return;

        _wantedCommand = command == null || SelectedCategory == null
            ? null
            : Walk(SelectedCategory.DetailNodes, SelectedCategory.Title)
                .FirstOrDefault(item => ReferenceEquals(item.Node, command)).Key;
        ApplyCommand(command);
    }

    /// <summary>Shows <paramref name="category"/> without changing what the user asked for.</summary>
    private void ApplyCategory(CommandCategoryViewModel category)
    {
        if (SelectedCategory != null)
            SelectedCategory.IsSelected = false;

        SelectedCategory = category;
        category.IsSelected = true;

        // Selecting a different category drops the previous command highlight.
        ApplyCommand(null);
    }

    /// <summary>Highlights <paramref name="command"/> without changing what the user asked for.</summary>
    private void ApplyCommand(CommandRowViewModel command)
    {
        if (SelectedCommand != null)
            SelectedCommand.IsSelected = false;

        SelectedCommand = command;

        if (command != null)
            command.IsSelected = true;
    }

    // ── Source projection ──────────────────────────────────────────────────

    private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnGroupChildrenChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnGroupPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MenuEntry.IsLoading) || sender is not MenuEntry group)
            return;

        foreach (var section in Sections)
            foreach (var category in section.Categories)
            {
                if (ReferenceEquals(category.Group, group))
                {
                    category.NotifyStateChanged();
                    return;
                }
            }
    }

    private void Rebuild()
    {
        // Plugin groups fill in asynchronously after the picker opens, and each of them lands here;
        // a catalogue refresh replaces every entry. The expand state is kept by name path and put
        // back — otherwise a group the user just opened snaps shut.
        foreach (var category in Sections.SelectMany(s => s.Categories))
        {
            foreach ((string key, object node) in Walk(category.DetailNodes, category.Title))
            {
                if (node is CommandGroupNodeViewModel group)
                    _expansion[key] = group.IsExpanded;
            }
        }

        UnsubscribeGroups();
        Sections.Clear();
        _searchLeaves.Clear();
        SelectedCategory = null;
        SelectedCommand = null;

        var sectionMap = SectionDefs.ToDictionary(
            d => d.Section,
            d => new CommandSectionViewModel(d.Section, d.Title, d.Icon));

        foreach (var group in _source)
        {
            var section = group.Section ?? CommandGroupSection.Plugins;
            if (!sectionMap.TryGetValue(section, out var sectionVm))
                sectionVm = sectionMap[CommandGroupSection.Plugins];

            var icon = string.IsNullOrEmpty(group.Icon) ? PluginFallbackIcon : group.Icon;
            var categoryVm = new CommandCategoryViewModel(group, icon);
            sectionVm.Categories.Add(categoryVm);

            // Flatten every leaf in the category subtree for search.
            CollectLeaves(group, LocalizationManager.Instance.TrText(group.Name, group.OwnerPluginId), icon, _searchLeaves);

            SubscribeGroup(group);
        }

        foreach (var def in SectionDefs)
        {
            var sectionVm = sectionMap[def.Section];
            if (sectionVm.Categories.Count > 0)
                Sections.Add(sectionVm);
        }

        foreach (var category in Sections.SelectMany(s => s.Categories))
        {
            foreach ((string key, object node) in Walk(category.DetailNodes, category.Title))
            {
                if (node is CommandGroupNodeViewModel group && _expansion.TryGetValue(key, out bool expanded))
                    group.IsExpanded = expanded;
            }
        }

        RestoreSelection();
        RestoreCommand();

        if (IsSearching)
            ApplyFilter();
    }

    /// <summary>
    /// Every row and group node below <paramref name="nodes"/>, keyed by its name path from the
    /// category (<paramref name="path"/>); a row's key also carries its command string, so two
    /// rows of the same name stay apart.
    /// </summary>
    private static IEnumerable<(string Key, object Node)> Walk(IEnumerable<object> nodes, string path)
    {
        foreach (object node in nodes)
        {
            switch (node)
            {
                case CommandRowViewModel row:
                    yield return ($"{path}/{row.Entry.Name}|{row.Entry.Command}", row);
                    break;
                case CommandGroupNodeViewModel group:
                    string groupPath = $"{path}/{group.Group.Name}";
                    yield return (groupPath, group);
                    foreach ((string Key, object Node) child in Walk(group.Children, groupPath))
                        yield return child;
                    break;
            }
        }
    }

    /// <summary>
    /// Re-highlights the command the user selected, if the selected category still holds it. If it
    /// is gone, nothing is highlighted; the groups above it stay open.
    /// </summary>
    private void RestoreCommand()
    {
        if (_wantedCommand == null || SelectedCategory == null)
            return;

        if (Walk(SelectedCategory.DetailNodes, SelectedCategory.Title)
                .FirstOrDefault(item => item.Key == _wantedCommand).Node is CommandRowViewModel row)
            ApplyCommand(row);
    }

    /// <summary>
    /// Shows the category the user selected. While it is missing (a plugin group still loading, or
    /// gone) the first category stands in, and the user's choice is kept for the next rebuild.
    /// </summary>
    private void RestoreSelection()
    {
        IEnumerable<CommandCategoryViewModel> categories = Sections.SelectMany(s => s.Categories);
        CommandCategoryViewModel target = categories.FirstOrDefault(c => c.Title == _wantedCategory)
                                          ?? categories.FirstOrDefault();
        if (target != null)
            ApplyCategory(target);
    }

    /// <summary>
    /// Walks a category subtree, emitting one <see cref="SearchLeaf"/> per insertable
    /// command and remembering the group path (e.g. "Audio / Realtek Speakers") so
    /// search results can be grouped by that path.
    /// </summary>
    private static void CollectLeaves(MenuEntry node, string path, string icon, List<SearchLeaf> output)
    {
        foreach (MenuEntry child in node.Children)
        {
            if (child.IsGroup())
            {
                var childIcon = string.IsNullOrEmpty(child.Icon) ? icon : child.Icon;
                CollectLeaves(child, $"{path} / {LocalizationManager.Instance.TrText(child.Name, child.OwnerPluginId)}", childIcon, output);
            }
            else if (child.IsCommand())
            {
                output.Add(new SearchLeaf(new CommandRowViewModel(child, icon), path, icon));
            }
        }
    }

    // ── Search ─────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        SearchResults.Clear();
        if (!IsSearching)
            return;

        var query = SearchText.Trim();

        // Preserve tree order while grouping matches by their group path.
        var groups = new Dictionary<string, CommandSearchGroupViewModel>(StringComparer.Ordinal);

        foreach (var leaf in _searchLeaves)
        {
            if (!Matches(leaf.Row, query))
                continue;

            if (!groups.TryGetValue(leaf.GroupPath, out var group))
            {
                group = new CommandSearchGroupViewModel(leaf.GroupPath, leaf.GroupIcon, []);
                groups[leaf.GroupPath] = group;
                SearchResults.Add(group);
            }

            group.Commands.Add(leaf.Row);
        }
    }

    private static bool Matches(CommandRowViewModel row, string query) =>
        row.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (row.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    // ── Subscription bookkeeping ─────────────────────────────────────────────

    private void SubscribeGroup(MenuEntry group)
    {
        group.Children.CollectionChanged += OnGroupChildrenChanged;
        group.PropertyChanged += OnGroupPropertyChanged;
        _subscribedGroups.Add(group);
    }

    private void UnsubscribeGroups()
    {
        foreach (var group in _subscribedGroups)
        {
            group.Children.CollectionChanged -= OnGroupChildrenChanged;
            group.PropertyChanged -= OnGroupPropertyChanged;
        }

        _subscribedGroups.Clear();
    }
}
