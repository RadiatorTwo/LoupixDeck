using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace LoupixDeck.Services.Notices;

/// <inheritdoc cref="INoticeService"/>
public sealed class NoticeService : INoticeService
{
    private readonly ObservableCollection<Notice> _notices = [];

    public NoticeService()
    {
        Notices = new ReadOnlyObservableCollection<Notice>(_notices);
    }

    public ReadOnlyObservableCollection<Notice> Notices { get; }

    public void Post(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        OnUiThread(() =>
        {
            notice.DismissCommand = new RelayCommand(() => Dismiss(notice));

            int index = IndexOf(notice.Id);
            if (index >= 0)
                _notices[index] = notice;
            else
                _notices.Add(notice);
        });
    }

    public void Remove(string id)
    {
        OnUiThread(() =>
        {
            int index = IndexOf(id);
            if (index >= 0)
                _notices.RemoveAt(index);
        });
    }

    private void Dismiss(Notice notice)
    {
        // Only the notice still on screen counts: a stale command of a replaced notice does nothing.
        int index = IndexOf(notice.Id);
        if (index < 0 || !ReferenceEquals(_notices[index], notice))
            return;

        _notices.RemoveAt(index);

        try
        {
            notice.OnDismissed?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NoticeService: dismiss handler of '{notice.Id}' threw: {ex.Message}");
        }
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < _notices.Count; i++)
            if (string.Equals(_notices[i].Id, id, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
