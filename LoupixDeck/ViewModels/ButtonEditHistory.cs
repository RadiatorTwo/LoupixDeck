using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Services.History;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Undo/redo for a button editor (issue #622). The editor reports every change through
/// <see cref="NotifyChanged"/>; once the edits pause, the button's content is captured and, when
/// it differs from the last capture, recorded as one step. A burst of changes (a layer dragged
/// across the canvas, a slider pulled) therefore becomes a single step. Undo and redo hand the
/// recorded content back to the editor to apply.
/// </summary>
public sealed class ButtonEditHistory : IDisposable
{
    private sealed record Step(string Before, string After);

    // How long the edits have to pause before they are recorded.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<string> _capture;
    private readonly Action<string> _apply;
    private readonly UndoStack<Step> _stack = new();
    private readonly DispatcherTimer _timer;
    private string _baseline;
    private bool _applying;

    /// <param name="capture">Returns the edited button's content as JSON.</param>
    /// <param name="apply">Puts recorded content back onto the button and refreshes the editor.</param>
    public ButtonEditHistory(Func<string> capture, Action<string> apply)
    {
        _capture = capture;
        _apply = apply;
        _baseline = capture();
        _timer = new DispatcherTimer { Interval = SettleDelay };
        _timer.Tick += (_, _) => Flush();
        _stack.Changed += RaiseCanExecuteChanged;

        UndoCommand = new RelayCommand(Undo, () => _stack.CanUndo || _timer.IsEnabled);
        RedoCommand = new RelayCommand(Redo, () => _stack.CanRedo);
    }

    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    /// <summary>Reports a change to the edited button. Safe to call from any thread.</summary>
    public void NotifyChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(NotifyChanged);
            return;
        }

        if (_applying) return;

        bool wasPending = _timer.IsEnabled;
        _timer.Stop();
        _timer.Start();
        if (!wasPending)
            UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Records the edits made since the last step right away.</summary>
    public void Flush()
    {
        bool wasPending = _timer.IsEnabled;
        _timer.Stop();

        string now = _capture();
        if (now != null && now != _baseline)
        {
            _stack.Push(new Step(_baseline, now));
            _baseline = now;
        }
        else if (wasPending)
        {
            UndoCommand.NotifyCanExecuteChanged();
        }
    }

    private void Undo()
    {
        Flush();
        Step step = _stack.Undo();
        if (step != null)
            Apply(step.Before);
    }

    private void Redo()
    {
        // An edit made after the undo starts a new branch, which drops the redo side.
        Flush();
        Step step = _stack.Redo();
        if (step != null)
            Apply(step.After);
    }

    private void Apply(string content)
    {
        _applying = true;
        try
        {
            _apply(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ButtonEditHistory] Applying a recorded step failed: {ex}");
        }
        finally
        {
            _applying = false;
        }

        // The refresh the apply caused is not an edit.
        _timer.Stop();
        _baseline = _capture();
        RaiseCanExecuteChanged();
    }

    private void RaiseCanExecuteChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _timer.Stop();
        _stack.Changed -= RaiseCanExecuteChanged;
    }
}
