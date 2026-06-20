#nullable enable
using CommunityToolkit.Mvvm.Input;

namespace LoupixDeck.Utils;

#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
public static partial class Relay
{
    /// <inheritdoc cref="RelayCommand(Action)"/>
    public static RelayCommand Create(Action execute) => new(execute);

    /// <inheritdoc cref="RelayCommand(Action, Func{bool})"/>
    public static RelayCommand Create(Action execute, Func<bool> canExecute) => new(execute, canExecute);

    /// <inheritdoc cref="RelayCommand{T}.RelayCommand(Action{T})"/>
    public static RelayCommand<T> Create<T>(Action<T> execute) => new(execute);

    /// <inheritdoc cref="RelayCommand{T}.RelayCommand(Action{T}, Predicate{T})"/>
    public static RelayCommand<T> Create<T>(Action<T> execute, Predicate<T> canExecute) => new(execute, canExecute);

    /// <inheritdoc cref="AsyncRelayCommand(Func{Task}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand Create(Func<Task> execute, AsyncRelayCommandOptions options = default) => new(execute, options);

    /// <inheritdoc cref="AsyncRelayCommand(Func{Task}, Func{bool}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand Create(Func<Task> execute, Func<bool> canExecute, AsyncRelayCommandOptions options = default) => new(execute, canExecute, options);

    /// <inheritdoc cref="AsyncRelayCommand{T}.AsyncRelayCommand(Func{T, Task}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand<T> Create<T>(Func<T, Task> execute, AsyncRelayCommandOptions options = default) => new(execute, options);

    /// <inheritdoc cref="AsyncRelayCommand{T}.AsyncRelayCommand(Func{T, Task}, Predicate{T}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand<T> Create<T>(Func<T?, Task> execute, Predicate<T?> canExecute, AsyncRelayCommandOptions options = default) => new(execute, canExecute, options);

    #region Same as above AsyncRelayCommand, but featuring CancellationToken

    /// <inheritdoc cref="AsyncRelayCommand(Func{CancellationToken, Task}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand Create(Func<CancellationToken, Task> execute, AsyncRelayCommandOptions options = default) => new(execute, options);

    /// <inheritdoc cref="AsyncRelayCommand(Func{CancellationToken, Task}, Func{bool}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand Create(Func<CancellationToken, Task> execute, Func<bool> canExecute, AsyncRelayCommandOptions options = default) => new(execute, canExecute, options);

    /// <inheritdoc cref="AsyncRelayCommand{T}.AsyncRelayCommand(Func{T, CancellationToken, Task}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand<T> Create<T>(Func<T, CancellationToken, Task> execute, AsyncRelayCommandOptions options = default) => new(execute, options);

    /// <inheritdoc cref="AsyncRelayCommand{T}.AsyncRelayCommand(Func{T, CancellationToken, Task}, Predicate{T}, AsyncRelayCommandOptions)"/>
    public static AsyncRelayCommand<T> Create<T>(Func<T, CancellationToken, Task> execute, Predicate<T> canExecute, AsyncRelayCommandOptions options = default) => new(execute, canExecute, options);

    #endregion
}