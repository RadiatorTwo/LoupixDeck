using LoupixDeck.Models;

namespace LoupixDeck.Utils;

/// <summary>Finds a button page in a page list by its stable id, with a legacy index as fallback.</summary>
public static class PageLookup
{
    /// <summary>
    /// The index of the page with <paramref name="pageId"/> in <paramref name="pages"/>. When the id
    /// is not given or does not resolve, <paramref name="pageIndex"/> applies if it is in range.
    /// Null when neither resolves.
    /// </summary>
    public static int? ResolveIndex<TPage>(IList<TPage> pages, Guid? pageId, int? pageIndex)
        where TPage : ButtonPageBase
    {
        if (pages == null) return null;

        if (pageId is { } id)
        {
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Id == id) return i;
        }

        return pageIndex is { } index && index >= 0 && index < pages.Count ? index : null;
    }
}
