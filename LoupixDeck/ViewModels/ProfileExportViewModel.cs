using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Portable;
using LoupixDeck.Services;
using LoupixDeck.Services.Portable;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>What an export dialog writes: one profile, workspace or page, and its display name.</summary>
public sealed class ProfileExportRequest
{
    private ProfileExportRequest(PackageKind kind, string name, Func<IProfilePackageService, string, string, Task<ProfilePackageResult>> export)
    {
        Kind = kind;
        Name = name;
        Export = export;
    }

    public PackageKind Kind { get; }

    public string Name { get; }

    /// <summary>Writes the item: (service, target path, description) → result.</summary>
    internal Func<IProfilePackageService, string, string, Task<ProfilePackageResult>> Export { get; }

    public static ProfileExportRequest ForProfile(Profile profile) =>
        new(PackageKind.Profile, profile.Name, (service, path, description) => service.ExportProfileAsync(profile, path, description));

    public static ProfileExportRequest ForWorkspace(Workspace workspace) =>
        new(PackageKind.Workspace, workspace.Name, (service, path, description) => service.ExportWorkspaceAsync(workspace, path, description));

    public static ProfileExportRequest ForTouchPage(TouchButtonPage page) =>
        new(PackageKind.TouchPage, page.PageName, (service, path, description) => service.ExportTouchPageAsync(page, path, description));

    public static ProfileExportRequest ForRotaryPage(RotaryButtonPage page) =>
        new(PackageKind.RotaryPage, page.PageName, (service, path, description) => service.ExportRotaryPageAsync(page, path, description));
}

/// <summary>
/// Export dialog for a <c>.loupixprofile</c> package: an optional description, the output file
/// (prefilled with the last export folder and a file name from the item) and the export itself.
/// Every export in the app goes through here.
/// </summary>
public sealed partial class ProfileExportViewModel : DialogViewModelBase<DialogResult>
{
    /// <summary><c>ui-settings.json</c> key of the folder the last successful export went to.</summary>
    private const string LastExportFolderKey = "LastExportFolder";

    private readonly IProfilePackageService _packageService;
    private ProfileExportRequest _request;

    public ProfileExportViewModel(IProfilePackageService packageService)
    {
        _packageService = packageService;
    }

    /// <summary>
    /// Shows the export dialog for <paramref name="request"/> and returns the export result, or null
    /// when the user cancelled. Shared by every place that offers an export.
    /// </summary>
    public static async Task<ProfilePackageResult> ShowAsync(IDialogService dialogService, ProfileExportRequest request)
    {
        ProfileExportViewModel exportViewModel = null;
        DialogResult dialogResult = await dialogService.ShowDialogAsync<ProfileExportViewModel, DialogResult>(vm =>
        {
            exportViewModel = vm;
            vm.Configure(request);
        });

        return dialogResult?.IsConfirmed == true ? exportViewModel?.Result : null;
    }

    public void Configure(ProfileExportRequest request)
    {
        _request = request;
        ItemName = request.Name;
        KindText = request.Kind switch
        {
            PackageKind.Profile => Loc.Tr("ProfileExport_KindProfile"),
            PackageKind.Workspace => Loc.Tr("ProfileExport_KindWorkspace"),
            PackageKind.TouchPage => Loc.Tr("ProfileExport_KindTouchPage"),
            PackageKind.RotaryPage => Loc.Tr("ProfileExport_KindRotaryPage"),
            _ => request.Kind.ToString()
        };
        OutputPath = Path.Combine(DefaultFolder(), FileDialogHelper.SuggestPackageFileName(request.Name));
    }

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    /// <summary>The export result; set when the export succeeded and the dialog closed.</summary>
    public ProfilePackageResult Result { get; private set; }

    [ObservableProperty]
    public partial string KindText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ItemName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileExists))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string OutputPath { get; set; } = string.Empty;

    /// <summary>The user agreed to replace an existing file (in the dialog or in the save picker).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool OverwriteExisting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string ErrorMessage { get; set; }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>The result message and its notes after an export that succeeded with warnings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial string CompletedNotes { get; set; }

    /// <summary>True once the package is written and the dialog only offers Close.</summary>
    public bool IsCompleted => CompletedNotes != null;

    public bool IsEditable => !IsCompleted;

    /// <summary>True when the output path names a file that is already there.</summary>
    public bool FileExists
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(OutputPath) && File.Exists(WithExtension(OutputPath));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool CanExport => !IsExporting && !string.IsNullOrWhiteSpace(OutputPath) && (!FileExists || OverwriteExisting);

    public IAsyncRelayCommand BrowseCommand => field ??= Relay.Create(BrowseAsync);

    public IAsyncRelayCommand ExportCommand => field ??= Relay.Create(ExportAsync, () => CanExport);

    public IRelayCommand CloseCommand => field ??= Relay.Create(Close);

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    partial void OnOutputPathChanged(string value)
    {
        // A confirmation belongs to the file it was given for.
        OverwriteExisting = false;
        ErrorMessage = null;
    }

    private async Task BrowseAsync()
    {
        string current = OutputPath?.Trim();
        string fileName = string.IsNullOrEmpty(current)
            ? FileDialogHelper.SuggestPackageFileName(_request?.Name)
            : Path.GetFileName(current);

        string picked = await FileDialogHelper.SaveProfilePackageDialog(WindowHelper.GetActiveWindow(), fileName);
        if (string.IsNullOrEmpty(picked))
            return;

        OutputPath = picked;

        // The save picker already asked before handing back an existing file.
        OverwriteExisting = FileExists;
    }

    private async Task ExportAsync()
    {
        if (_request == null || !CanExport)
            return;

        string target;
        try
        {
            target = Path.GetFullPath(WithExtension(OutputPath.Trim()));
            string folder = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ErrorMessage = Loc.Tr("ProfileExport_FolderMissing");
                return;
            }
        }
        catch (Exception)
        {
            ErrorMessage = Loc.Tr("ProfileExport_InvalidPath");
            return;
        }

        IsExporting = true;
        ErrorMessage = null;

        try
        {
            string description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            ProfilePackageResult result = await _request.Export(_packageService, target, description);
            if (!result.Success)
            {
                ErrorMessage = result.Message;
                return;
            }

            UiSettingsStore.Set(LastExportFolderKey, Path.GetDirectoryName(target));
            Result = result;
        }
        finally
        {
            IsExporting = false;
        }

        // Not every caller has a place to show notes (the header menus do not), so the dialog stays
        // open with them until the user closes it.
        if (Result.Warnings.Count > 0)
        {
            CompletedNotes = string.Join(Environment.NewLine, [Result.Message, .. Result.Warnings]);
            return;
        }

        Close();
    }

    private void Close()
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    }

    /// <summary>The last export folder while it still exists, else Documents, else the home folder.
    /// Documents can be unset on Linux.</summary>
    private static string DefaultFolder()
    {
        string last = UiSettingsStore.GetString(LastExportFolderKey);
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
            return last;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
            return documents;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string WithExtension(string path) =>
        string.Equals(Path.GetExtension(path), "." + ProfilePackageFiles.Extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + "." + ProfilePackageFiles.Extension;
}
