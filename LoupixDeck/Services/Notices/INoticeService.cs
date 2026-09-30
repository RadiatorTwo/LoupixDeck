using System.Collections.ObjectModel;

namespace LoupixDeck.Services.Notices;

/// <summary>
/// The app-wide list of notices the main window shows in its strip under the device switcher:
/// the app update hint, the plugin update hint, unmet plugin requirements. A reusable way to
/// tell the user something without a modal dialog. Root singleton; every method may be called
/// from any thread and applies its change on the UI thread.
/// </summary>
public interface INoticeService
{
    /// <summary>The notices currently shown, in the order they were first posted. Bound by the
    /// strip; only changes on the UI thread.</summary>
    ReadOnlyObservableCollection<Notice> Notices { get; }

    /// <summary>Shows <paramref name="notice"/>, replacing a notice with the same id in place.</summary>
    void Post(Notice notice);

    /// <summary>Removes the notice with the given id; does nothing when there is none.</summary>
    void Remove(string id);
}
