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
            string? selectedFolder = _folderPicker.PickFolder("Choose an empty folder for the new project");
            if (selectedFolder is not null)
            {
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
        Result = new CreateProjectRequest(ProjectName, SeriesName, ProjectFolder);
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }
}
