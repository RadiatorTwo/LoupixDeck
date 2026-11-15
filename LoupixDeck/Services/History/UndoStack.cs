namespace LoupixDeck.Services.History;

/// <summary>
/// Bounded undo/redo stacks of recorded steps (issue #622). A new step clears the redo side;
/// undoing a step moves it to the redo side and back. The oldest step is dropped once
/// <see cref="Capacity"/> is reached.
/// </summary>
public sealed class UndoStack<T> where T : class
{
    /// <summary>Most steps kept on the undo side.</summary>
    public const int Capacity = 100;

    private readonly LinkedList<T> _undo = new();
    private readonly Stack<T> _redo = new();

    /// <summary>Raised after either side changed.</summary>
    public event Action Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Every step held on either side.</summary>
    public IEnumerable<T> Steps => _undo.Concat(_redo);

    public void Push(T step)
    {
        _undo.AddLast(step);
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Takes the newest step off the undo side, or null when there is none.</summary>
    public T Undo()
    {
        if (_undo.Last == null) return null;
        T step = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push(step);
        Changed?.Invoke();
        return step;
    }

    /// <summary>Takes the newest undone step back, or null when there is none.</summary>
    public T Redo()
    {
        if (_redo.Count == 0) return null;
        T step = _redo.Pop();
        _undo.AddLast(step);
        Changed?.Invoke();
        return step;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
