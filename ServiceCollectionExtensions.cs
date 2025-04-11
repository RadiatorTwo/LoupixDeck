using AutoMapper;
using LoupixDeck.Models;
using LoupixDeck.Models.Mapper;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        var elgatoDevices = ElgatoDevices.LoadFromFile();
        collection.AddSingleton(elgatoDevices ?? new ElgatoDevices());

        collection.AddSingleton<ICommandBuilder, CommandBuilder>();
        collection.AddSingleton<ISysCommandService, SysCommandService>();
        collection.AddSingleton<IUInputKeyboard, UInputKeyboard>();

        collection.AddSingleton<ObsController>();
        collection.AddSingleton<DBusController>();
        collection.AddSingleton<CommandRunner>();
        collection.AddSingleton<ElgatoController>();

        collection.AddSingleton<LoupedeckLiveS>();

        collection.AddTransient<MainWindowViewModel>();

        InitMapper(collection);
    }

    private static void InitMapper(this IServiceCollection collection)
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<ButtonMappingProfile>();
        });

        var mapper = config.CreateMapper();
        
        collection.AddSingleton(mapper);
    }
}