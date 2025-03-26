using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<ObsController>();
        collection.AddSingleton<DBusController>();
        collection.AddSingleton<CommandRunner>();
        collection.AddSingleton<ElgatoController>();
        
        collection.AddTransient<MainWindowViewModel>();
    }
}