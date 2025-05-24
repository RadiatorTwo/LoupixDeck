using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services;

public interface IDialogService
{
    Task<DialogResult> ShowDialogAsync<TViewModel, TResult>(Action<TViewModel> initializer = null)
        where TViewModel : IDialogViewModel;

    void Register<TViewModel, TWindow>()
        where TWindow : Window;
}

public class DialogService(IServiceProvider serviceProvider) : IDialogService
{
    private readonly Dictionary<Type, Type> _viewModelToWindowMap = new();

    public void Register<TViewModel, TWindow>()
        where TWindow : Window
    {
        _viewModelToWindowMap[typeof(TViewModel)] = typeof(TWindow);
    }

    public async Task<DialogResult> ShowDialogAsync<TViewModel, TResult>(Action<TViewModel> initializer = null)
        where TViewModel : IDialogViewModel
    {
        var viewModel = serviceProvider.GetRequiredService<TViewModel>();
        initializer?.Invoke(viewModel);

        if (!_viewModelToWindowMap.TryGetValue(typeof(TViewModel), out var windowType))
            throw new InvalidOperationException($"No window registered for {typeof(TViewModel).Name}");

        var window = (Window)Activator.CreateInstance(windowType)!;
        window.DataContext = viewModel;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        await window.ShowDialog(WindowHelper.GetMainWindow());
        return await viewModel.DialogResult.Task;
    }
}