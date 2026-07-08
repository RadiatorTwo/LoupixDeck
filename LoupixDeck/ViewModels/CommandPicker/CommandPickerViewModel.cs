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
/// Reusable view-model for the card-based command picker (issue #171). It projects
/// the host's <c>SystemCommandMenus</c> (<see cref="MenuEntry"/> group→leaf tree,
/// filled by <c>IMenuTreeBuilder.BuildInto</c>) into a sectioned category-card grid
/// with a searchable command list. Categories can nest arbitrarily deep (e.g. Audio's
/// <c>device → commands</c>); the detail area shows those sub-groups as inline
/// accordion sections. Selection and search live here; command insertion stays with
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

    /// <summary>Sections shown as the card grid when not searching.</summary>
    public ObservableCollection<CommandSectionViewModel> Sections { get; } = [];

    /// <summary>Grouped search results shown instead of the card grid while searching.</summary>
    public ObservableCollection<CommandSearchGroupViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCategory))]
    public partial CommandCategoryViewModel SelectedCategory { get; set; }

    [ObservableProperty]
    public partial CommandRowViewModel SelectedCommand { get; set; }

    public bool HasSelectedCategory => SelectedCategory != null;

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsSearching));
                ApplyFilter();
            }
        }
    } = string.Empty;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

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

        if (SelectedCategory != null)
            SelectedCategory.IsSelected = false;

        SelectedCategory = category;
        category.IsSelected = true;

        // Selecting a different category drops the previous command highlight.
        SelectCommand(null);
    }

    public void SelectCommand(CommandRowViewModel command)
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
        var previousCategory = SelectedCategory?.Title;

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
            CollectLeaves(group, group.Name, icon, _searchLeaves);

            SubscribeGroup(group);
        }

        foreach (var def in SectionDefs)
        {
            var sectionVm = sectionMap[def.Section];
            if (sectionVm.Categories.Count > 0)
                Sections.Add(sectionVm);
        }

        RestoreSelection(previousCategory);

        if (IsSearching)
            ApplyFilter();
    }

    private void RestoreSelection(string categoryTitle)
    {
        CommandCategoryViewModel target = null;

        if (categoryTitle != null)
        {
            target = Sections
                .SelectMany(s => s.Categories)
                .FirstOrDefault(c => c.Title == categoryTitle);
        }

        // Default to the first category so the detail area is never empty on open.
        target ??= Sections.SelectMany(s => s.Categories).FirstOrDefault();
        if (target != null)
            SelectCategory(target);
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
                CollectLeaves(child, $"{path} / {child.Name}", childIcon, output);
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
