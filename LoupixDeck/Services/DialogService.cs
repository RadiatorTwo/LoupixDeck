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

        // Prefer a (ViewModel) ctor so the window can set DataContext *before*
        // InitializeComponent runs — that prevents spurious binding warnings on
        // $parent[Window].DataContext.X during the first XAML evaluation pass.
        Window window;
        var ctorWithVm = windowType.GetConstructor(new[] { typeof(TViewModel) });
        if (ctorWithVm != null)
        {
            window = (Window)ctorWithVm.Invoke(new object[] { viewModel })!;
        }
        else
        {
            window = (Window)Activator.CreateInstance(windowType)!;
            window.DataContext = viewModel;
        }
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (viewModel is IAsyncInitViewModel asyncInit)
        {
            // Kick off async init without blocking. The sync prefix of
            // InitializeAsync (before its first await) runs immediately so
            // bindings that read initial collection references stay valid.
            _ = asyncInit.InitializeAsync();
        }

        await window.ShowDialog(WindowHelper.GetMainWindow());

        // The window is gone, so nothing can set the result any more. A dialog closed by
        // the title bar's X — or one that simply closes without confirming, like About —
        // never called Confirm/Cancel, and awaiting its task below would then hang
        // forever: the caller's AsyncRelayCommand stays "running", which leaves the menu
        // entry that opened it permanently disabled (issue #201). Treat any such close as
        // a cancel; a result the view model already set wins (TrySetResult).
        viewModel.DialogResult.TrySetResult(Models.DialogResult.Cancel());

        return await viewModel.DialogResult.Task;
    }
}