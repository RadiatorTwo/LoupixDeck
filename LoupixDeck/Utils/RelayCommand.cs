using System.Windows.Input;

namespace LoupixDeck.Utils;

public sealed class RelayCommand<T>(Action<T> execute, Predicate<T> canExecute = null) : ICommand
{
    private readonly Action<T> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private bool _isExecuting;

    public bool CanExecute(object parameter)
    {
        if (_isExecuting) return false;
        if (canExecute == null) return true;
        // For reference types, allow null parameter to flow through the predicate;
        // value-type T's still require a successful cast.
        if (parameter is T param) return canExecute(param);
        return parameter == null && default(T) == null && canExecute(default);
    }

    public void Execute(object parameter)
    {
        if (!CanExecute(parameter)) return;

        _isExecuting = true;
        OnCanExecuteChanged();

        try
        {
            if (parameter is T param)
            {
                _execute(param);
            }
        }
        finally
        {
            _isExecuting = false;
            OnCanExecuteChanged();
        }
    }

    public event EventHandler CanExecuteChanged;

    public void RaiseCanExecuteChanged() => OnCanExecuteChanged();

    private void OnCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class RelayCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private bool _isExecuting;

    public bool CanExecute(object parameter) => !_isExecuting;

    public async void Execute(object parameter)
    {
        if (!CanExecute(parameter)) return;

        _isExecuting = true;
        OnCanExecuteChanged();

        try
        {
            await Task.Run(() => _execute());
        }
        finally
        {
            _isExecuting = false;
            OnCanExecuteChanged();
        }
    }

    public event EventHandler CanExecuteChanged;

    private void OnCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    private readonly Func<Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private bool _isExecuting;

    public bool CanExecute(object parameter) => !_isExecuting;

    public async void Execute(object parameter)
    {
        if (!CanExecute(parameter)) return;

        _isExecuting = true;
        OnCanExecuteChanged();

        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            OnCanExecuteChanged();
        }
    }

    public event EventHandler CanExecuteChanged;

    private void OnCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}