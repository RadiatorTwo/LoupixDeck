using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class Settings : Window
{
    public Settings() : this(null) { }

    public Settings(SettingsViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        PopulatePluginList();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
            {
                dlg.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    // ───────── Plugins page (generic, schema-driven settings form) ─────────

    private void PopulatePluginList()
    {
        if (DataContext is not SettingsViewModel vm || PluginList == null)
            return;

        PluginList.Items.Clear();
        foreach (var plugin in vm.Plugins)
        {
            PluginList.Items.Add(new ListBoxItem
            {
                Content = BuildPluginListEntry(vm, plugin),
                Tag = plugin
            });
        }
    }

    /// <summary>Builds a list row: an enable checkbox plus the plugin name/status.</summary>
    private Control BuildPluginListEntry(SettingsViewModel vm, LoadedPlugin plugin)
    {
        var name = plugin.Manifest?.Name ?? plugin.Directory;
        var label = plugin.Status == PluginLoadStatus.Loaded
            ? name
            : $"{name}  ({plugin.Status})";

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var id = plugin.Manifest?.Id;
        var enableBox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = id != null && IsPluginEnabled(vm, id),
            // A plugin with an unreadable manifest has no id — it cannot be toggled.
            IsEnabled = !string.IsNullOrEmpty(id)
        };
        enableBox.IsCheckedChanged += (_, _) => TogglePlugin(vm, id, enableBox.IsChecked == true);

        row.Children.Add(enableBox);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static bool IsPluginEnabled(SettingsViewModel vm, string id) =>
        vm.Config.EnabledPlugins.Any(e => string.Equals(e, id, System.StringComparison.OrdinalIgnoreCase));

    private void TogglePlugin(SettingsViewModel vm, string id, bool enabled)
    {
        if (string.IsNullOrEmpty(id))
            return;

        var list = vm.Config.EnabledPlugins;
        var has = IsPluginEnabled(vm, id);

        if (enabled && !has)
            list.Add(id);
        else if (!enabled && has)
            list.RemoveAll(e => string.Equals(e, id, System.StringComparison.OrdinalIgnoreCase));
        else
            return;

        // The change takes effect on the next launch — plugins load at startup.
        if (PluginRestartHint != null)
            PluginRestartHint.IsVisible = true;
    }

    private void OnPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginSettingsHost == null)
            return;

        PluginSettingsHost.Children.Clear();

        if (PluginList?.SelectedItem is not ListBoxItem { Tag: LoadedPlugin plugin })
            return;

        if (plugin.Status != PluginLoadStatus.Loaded)
        {
            PluginSettingsHost.Children.Add(new TextBlock
            {
                Text = plugin.FailureReason ?? plugin.Status.ToString(),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        if (plugin.Instance is not IPluginSettingsPage page || plugin.Host == null)
        {
            PluginSettingsHost.Children.Add(new TextBlock { Text = "This plugin has no settings." });
            return;
        }

        BuildSettingsForm(page, plugin.Host.Settings);
    }

    private void BuildSettingsForm(IPluginSettingsPage page, IPluginSettings settings)
    {
        var editors = new List<(PluginSettingDescriptor Descriptor, Control Control)>();

        foreach (var descriptor in page.SettingsSchema)
        {
            if (descriptor.Kind == PluginSettingKind.Heading)
            {
                PluginSettingsHost.Children.Add(new TextBlock
                {
                    Text = descriptor.Label,
                    FontWeight = FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 12, 0, 4)
                });
                if (!string.IsNullOrWhiteSpace(descriptor.Description))
                {
                    PluginSettingsHost.Children.Add(new TextBlock
                    {
                        Text = descriptor.Description,
                        FontSize = 11,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                continue;
            }

            PluginSettingsHost.Children.Add(new TextBlock { Text = descriptor.Label });

            Control editor;
            switch (descriptor.Kind)
            {
                case PluginSettingKind.Toggle:
                    editor = new CheckBox
                    {
                        IsChecked = settings.Get(descriptor.Key, descriptor.DefaultValue is true)
                    };
                    break;

                case PluginSettingKind.Number:
                    editor = new TextBox
                    {
                        Width = 160,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Text = settings.Get(descriptor.Key, ToLong(descriptor.DefaultValue))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    break;

                case PluginSettingKind.Password:
                    editor = new TextBox
                    {
                        Width = 280,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        PasswordChar = '*',
                        Text = settings.Get(descriptor.Key, descriptor.DefaultValue as string ?? string.Empty)
                    };
                    break;

                default:
                    editor = new TextBox
                    {
                        Width = 280,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Text = settings.Get(descriptor.Key, descriptor.DefaultValue as string ?? string.Empty)
                    };
                    break;
            }

            PluginSettingsHost.Children.Add(editor);

            if (!string.IsNullOrWhiteSpace(descriptor.Description))
            {
                PluginSettingsHost.Children.Add(new TextBlock
                {
                    Text = descriptor.Description,
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            editors.Add((descriptor, editor));
        }

        var status = new TextBlock { Margin = new Avalonia.Thickness(0, 4, 0, 0) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };

        var saveButton = new Button { Content = "Save", Width = 100 };
        saveButton.Click += (_, _) =>
        {
            foreach (var (descriptor, control) in editors)
                WriteValue(settings, descriptor, control);

            settings.Save();
            page.OnSettingsSaved();
            status.Text = "Saved.";
        };
        buttons.Children.Add(saveButton);

        foreach (var action in page.SettingsActions)
        {
            var actionButton = new Button { Content = action.Label, MinWidth = 120 };
            actionButton.Click += async (_, _) =>
            {
                // Persist edits first so the action runs against current values.
                foreach (var (descriptor, control) in editors)
                    WriteValue(settings, descriptor, control);
                settings.Save();
                page.OnSettingsSaved();

                status.Text = $"{action.Label}…";
                try
                {
                    status.Text = await action.Invoke();
                }
                catch (System.Exception ex)
                {
                    status.Text = $"Failed: {ex.Message}";
                }
            };
            buttons.Children.Add(actionButton);
        }

        PluginSettingsHost.Children.Add(buttons);
        PluginSettingsHost.Children.Add(status);
    }

    private static void WriteValue(IPluginSettings settings, PluginSettingDescriptor descriptor, Control control)
    {
        switch (descriptor.Kind)
        {
            case PluginSettingKind.Toggle:
                settings.Set(descriptor.Key, (control as CheckBox)?.IsChecked == true);
                break;

            case PluginSettingKind.Number:
                var raw = (control as TextBox)?.Text ?? string.Empty;
                long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var number);
                settings.Set(descriptor.Key, number);
                break;

            default:
                settings.Set(descriptor.Key, (control as TextBox)?.Text ?? string.Empty);
                break;
        }
    }

    private static long ToLong(object value)
    {
        try
        {
            return value == null ? 0L : System.Convert.ToInt64(value);
        }
        catch
        {
            return 0L;
        }
    }
}
