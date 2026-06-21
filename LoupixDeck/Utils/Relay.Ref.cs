#nullable enable

using CommunityToolkit.Mvvm.Input;

namespace LoupixDeck.Utils;

public static partial class Relay
{
    public static IRelayCommand Ref(ref IRelayCommand field, Action execute) => field ??= Create(execute);
    public static IRelayCommand Ref(ref IRelayCommand field, Action execute, Func<bool> canExecute) => field ??= Create(execute, canExecute);
    public static IRelayCommand<T> Ref<T>(ref IRelayCommand<T> field, Action<T> execute) => field ??= Create(execute);
    public static IRelayCommand<T> Ref<T>(ref IRelayCommand<T> field, Action<T> execute, Predicate<T> canExecute) => field ??= Create(execute, canExecute);
    public static IAsyncRelayCommand Ref(ref IAsyncRelayCommand field, Func<Task> execute) => field ??= Create(execute);
    public static IAsyncRelayCommand<T> Ref<T>(ref IAsyncRelayCommand<T> field, Func<T, Task> execute) => field ??= Create(execute);
    public static IAsyncRelayCommand<T> Ref<T>(ref IAsyncRelayCommand<T> field, Func<T, Task> execute, Predicate<T> canExecute) => field ??= Create(execute, canExecute);
}