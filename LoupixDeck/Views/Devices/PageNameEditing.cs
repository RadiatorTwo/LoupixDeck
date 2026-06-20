using Avalonia.Controls;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views.Devices;

/// <summary>
/// Shared commit logic for the page-name fields in the device-layout pagers. The
/// name binds two-way to the current page, so committing only needs to persist
/// the config. Used by both Live S and Razer layouts.
/// </summary>
internal static class PageNameEditing
{
    public static void Save(object sender)
    {
        if (sender is Control { DataContext: MainWindowViewModel vm })
            vm.LoupedeckController?.SaveConfig();
    }
}
