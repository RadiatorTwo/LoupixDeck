using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
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

    private async void TogglePlugin(SettingsViewModel vm, string id, bool enabled)
    {
        if (string.IsNullOrEmpty(id))
            return;

        // The reload coordinator owns the EnabledPlugins mutation and the live
        // load/unload. Lock the list during the await so rapid clicks can't race.
        if (PluginList != null)
            PluginList.IsEnabled = false;

        try
        {
            var result = enabled
                ? await vm.EnablePluginAsync(id)
                : await vm.DisablePluginAsync(id);
            ShowPluginActionResult(result);
        }
        finally
        {
            if (PluginList != null)
                PluginList.IsEnabled = true;
        }

        // Rebuild the rows so the status label ((Loaded)/(Disabled)) and the checkbox
        // reflect the post-reload reality.
        PopulatePluginList();
    }

    // ───────── Install / Remove (local, restart-based lifecycle) ─────────

    private async void OnInstallPluginClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var zipPath = await FileDialogHelper.OpenZipDialog(this);
        if (string.IsNullOrEmpty(zipPath))
            return;

        var result = await vm.InstallPluginFromZipAsync(zipPath);
        ShowPluginActionResult(result);

        // A freshly installed plugin loads live — rebuild the list so it appears.
        PopulatePluginList();
    }

    private async void OnRemovePluginClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        if (PluginList?.SelectedItem is not ListBoxItem { Tag: LoadedPlugin plugin })
        {
            ShowPluginActionStatus("Select a plugin in the list to remove.");
            return;
        }

        var name = plugin.Manifest?.Name ?? plugin.Directory;
        var confirmed = await ConfirmDialogHelper.AskYesNoAsync(this, "Remove plugin",
            $"Remove '{name}'? This deletes the plugin's folder and cannot be undone.");
        if (!confirmed)
            return;

        var result = await vm.RemovePluginAsync(plugin);

        if (result.Success)
        {
            // The plugin is already unloaded live; drop its row. The on-disk delete
            // (or a deferred one) and the enabled-list change persist on dialog close.
            if (PluginList.SelectedItem is ListBoxItem item)
                PluginList.Items.Remove(item);
            PluginSettingsHost?.Children.Clear();
        }

        ShowPluginActionResult(result);
    }

    private void ShowPluginActionResult(PluginActionResult result)
    {
        if (result == null)
            return;

        ShowPluginActionStatus(result.Message);

        if (result is { Success: true, RequiresRestart: true } && PluginRestartHint != null)
            PluginRestartHint.IsVisible = true;
    }

    private void ShowPluginActionStatus(string message)
    {
        if (PluginActionStatus == null)
            return;

        PluginActionStatus.Text = message ?? string.Empty;
        PluginActionStatus.IsVisible = !string.IsNullOrEmpty(message);
    }

    private void OnPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginSettingsHost == null)
            return;

        PluginSettingsHost.Children.Clear();

        if (PluginList?.SelectedItem is not ListBoxItem { Tag: LoadedPlugin plugin })
            return;

        // Manifest metadata header — shown for every plugin regardless of load status.
        BuildPluginHeader(plugin);

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

    /// <summary>
    /// Renders the manifest metadata (icon, name, author, version, description and
    /// project link) at the top of the detail pane. Every field is optional — a
    /// plugin whose manifest omits them simply shows less.
    /// </summary>
    private void BuildPluginHeader(LoadedPlugin plugin)
    {
        var manifest = plugin.Manifest;
        var name = manifest?.Name ?? plugin.Directory;

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        // Icon, if the manifest points at one that exists in the plugin folder.
        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.IconFile) &&
            !string.IsNullOrWhiteSpace(plugin.Directory))
        {
            var iconPath = System.IO.Path.Combine(plugin.Directory, manifest.IconFile);
            if (System.IO.File.Exists(iconPath))
            {
                try
                {
                    titleRow.Children.Add(new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(iconPath),
                        Width = 48,
                        Height = 48,
                        VerticalAlignment = VerticalAlignment.Top
                    });
                }
                catch
                {
                    // A broken/unsupported image must never break the settings page.
                }
            }
        }

        var titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeight.Bold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        });

        var subtitle = BuildSubtitle(manifest);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            titleText.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap
            });
        }

        titleRow.Children.Add(titleText);
        PluginSettingsHost.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(manifest?.Description))
        {
            PluginSettingsHost.Children.Add(new TextBlock
            {
                Text = manifest.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 4, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(manifest?.ProjectUrl))
        {
            var link = new Button
            {
                Content = "Open project page",
                Padding = new Avalonia.Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            // Accent blue (theme-invariant) — matches the active-page chip colour.
            link[!Button.ForegroundProperty] = new DynamicResourceExtension("PageActiveBrush");
            var url = manifest.ProjectUrl;
            link.Click += (_, _) => OpenUrl(url);
            PluginSettingsHost.Children.Add(link);
        }

        // Visual divider between the metadata header and the settings form / status.
        var divider = new Border
        {
            Height = 1,
            Margin = new Avalonia.Thickness(0, 8, 0, 4)
        };
        divider[!Border.BackgroundProperty] = new DynamicResourceExtension("AppBorderSubtle");
        PluginSettingsHost.Children.Add(divider);
    }

    /// <summary>Builds the "by {Author} • v{Version}" subtitle from whatever fields exist.</summary>
    private static string BuildSubtitle(PluginManifest manifest)
    {
        if (manifest == null)
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.Author))
            parts.Add($"by {manifest.Author}");
        if (!string.IsNullOrWhiteSpace(manifest.Version))
            parts.Add($"v{manifest.Version}");

        return parts.Count == 0 ? null : string.Join("  •  ", parts);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a browser is best-effort; failure must not crash the dialog.
        }
    }

    private void BuildSettingsForm(IPluginSettingsPage page, IPluginSettings settings, string initialStatus = null)
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

        var status = new TextBlock
        {
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            Text = initialStatus ?? string.Empty
        };

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
                string resultText;
                try
                {
                    resultText = await action.Invoke();
                }
                catch (System.Exception ex)
                {
                    resultText = $"Failed: {ex.Message}";
                }

                // Rebuild the form so dynamic schema/action changes are
                // reflected (e.g. an OAuth "Connect" button swaps to
                // "Disconnect" and the connection heading text updates).
                PluginSettingsHost.Children.Clear();
                BuildSettingsForm(page, settings, resultText);
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
