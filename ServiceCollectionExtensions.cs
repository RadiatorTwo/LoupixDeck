using LoupixDeck.Services;
using LoupixDeck.Utils;
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
        
        collection.AddTransient<MainWindowViewModel>();
    }
}