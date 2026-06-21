using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

[ObservableObject]
public abstract partial class ButtonPageBase
{
    /// <summary>
    /// Optional user-assigned page name. Persisted; when empty the page falls back
    /// to its number, so configs written before naming existed load unchanged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageName))]
    public partial string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public string PageName => string.IsNullOrWhiteSpace(Name) ? FormatPageName(Page) : Name;

    protected virtual string FormatPageName(int page) => $"Page: {page}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageName))]
    public partial int Page { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    public partial bool Selected { get; set; }
}