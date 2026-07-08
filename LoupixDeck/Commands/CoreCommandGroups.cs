using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;

// Card metadata (section, icon, description) for the built-in command groups shown
// in the command picker. Icons are MDI code points (see Utils/SymbolLibrary.cs);
// they are cosmetic only and never persisted. Plugin groups declare their own via
// LoupixPlugin.GetCommandGroups(); undeclared groups fall back to the Plugins
// section with a generic puzzle icon.

[assembly: CommandGroup("Pages", "Create and navigate your pages", "\U000F0214", CommandGroupSection.Core)]           // mdi-file
[assembly: CommandGroup("Device Control", "Control displays and devices", "\U000F0379", CommandGroupSection.Core)]    // mdi-monitor
[assembly: CommandGroup("Shell", "Run commands and scripts", "\U000F018D", CommandGroupSection.Core)]                 // mdi-console
[assembly: CommandGroup("Dynamic Text", "Live text on your buttons", "\U000F0954", CommandGroupSection.Core)]         // mdi-clock
[assembly: CommandGroup("Macros", "Prebuilt macros ready to use", "\U000F0570", CommandGroupSection.Macros)]          // mdi-view-grid
[assembly: CommandGroup("User Macros", "Your custom macros", "\U000F0004", CommandGroupSection.Macros)]               // mdi-account