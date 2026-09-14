using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Projects;
using Inklume.Desktop.Services;

namespace Inklume.Desktop.ViewModels;

public sealed partial class NewProjectDialogViewModel : ObservableObject
{
    private readonly IProjectFolderPicker _folderPicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _seriesName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _projectFolder = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public NewProjectDialogViewModel(IProjectFolderPicker folderPicker)
    {
        ArgumentNullException.ThrowIfNull(folderPicker);
        _folderPicker = folderPicker;
        BrowseCommand = new RelayCommand(Browse);
        CreateCommand = new RelayCommand(Create, CanCreate);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(false)));
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(_projectName) or nameof(_seriesName) or nameof(_projectFolder)
                or "ProjectName" or "SeriesName" or "ProjectFolder")
            {
                ErrorMessage = string.Empty;
            }
        };
    }

    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    public CreateProjectRequest? Result { get; private set; }

    public IRelayCommand BrowseCommand { get; }

    public IRelayCommand CreateCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(ProjectName)
        && !string.IsNullOrWhiteSpace(SeriesName)
        && !string.IsNullOrWhiteSpace(ProjectFolder);

    private void Browse()
    {
        try
        {
            ErrorMessage = string.Empty;
            string? selectedFolder = _folderPicker.PickFolder("Choose an empty folder for the new project");
            if (selectedFolder is not null)
            {
                if (Directory.Exists(selectedFolder)
                    && Directory.EnumerateFileSystemEntries(selectedFolder).Any()
                    && !string.IsNullOrWhiteSpace(ProjectName))
                {
                    string safeName = SanitizeFolderName(ProjectName);
                    string candidate = Path.Combine(selectedFolder, safeName);
                    if (!Directory.Exists(candidate) || !Directory.EnumerateFileSystemEntries(candidate).Any())
                    {
                        ProjectFolder = candidate;
                        return;
                    }
                }

                ProjectFolder = selectedFolder;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("New project folder selection failed: {0}", exception);
            ErrorMessage = "The folder picker could not be opened. Please try again.";
        }
    }

    private void Create()
    {
        ErrorMessage = string.Empty;
        string folder = ProjectFolder.Trim();

        try
        {
            if (!Path.IsPathRooted(folder))
            {
                ErrorMessage = "Please provide an absolute path for the project folder.";
                return;
            }

            string fullPath = Path.GetFullPath(folder);
            if (File.Exists(fullPath))
            {
                ErrorMessage = "The selected path is an existing file, not a folder.";
                return;
            }

            if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                ErrorMessage = "The selected folder is not empty. Please choose an empty folder or specify a new subfolder name.";
                return;
            }

            string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath));
            if (parent is not null && !Directory.Exists(parent))
            {
                ErrorMessage = "The parent folder must exist before creating a project.";
                return;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Invalid folder path: {exception.Message}";
            return;
        }

        Result = new CreateProjectRequest(ProjectName.Trim(), SeriesName.Trim(), folder);
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }

    private static string SanitizeFolderName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string clean = string.Concat(name.Trim().Select(c => invalidChars.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(clean) ? "Project" : clean;
    }
}
