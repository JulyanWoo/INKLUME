using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Projects;
using Inklume.Desktop.Services;

namespace Inklume.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ProjectService _projectService;
    private readonly IProjectFolderPicker _folderPicker;
    private CancellationTokenSource? _operationCancellation;
    private bool _isClosing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _seriesName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _projectFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseProjectFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private ProjectWorkspace? _currentWorkspace;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public MainViewModel(ProjectService projectService, IProjectFolderPicker folderPicker)
    {
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(folderPicker);
        _projectService = projectService;
        _folderPicker = folderPicker;

        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync, CanCreateProject);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync, CanBeginOperation);
        BrowseProjectFolderCommand = new RelayCommand(BrowseProjectFolder, CanBeginOperation);
        CancelOperationCommand = new RelayCommand(CancelOperation, CanCancelOperation);
    }

    public string ApplicationName { get; } = "INKLUME";

    public string Subtitle { get; } = "AI Comic Localization Studio";

    public string WorkspaceMessage { get; } = "No project is open in this workspace.";

    public bool IsIdle => !IsBusy && !_isClosing;

    public IAsyncRelayCommand CreateProjectCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IRelayCommand BrowseProjectFolderCommand { get; }

    public IRelayCommand CancelOperationCommand { get; }

    public async Task StopAsync()
    {
        _isClosing = true;
        OnPropertyChanged(nameof(IsIdle));
        CreateProjectCommand.NotifyCanExecuteChanged();
        OpenProjectCommand.NotifyCanExecuteChanged();
        BrowseProjectFolderCommand.NotifyCanExecuteChanged();
        CancelOperation();

        await Task.WhenAll(
            CreateProjectCommand.ExecutionTask ?? Task.CompletedTask,
            OpenProjectCommand.ExecutionTask ?? Task.CompletedTask);
    }

    private bool CanBeginOperation() => IsIdle;

    private bool CanCreateProject() => IsIdle
        && !string.IsNullOrWhiteSpace(ProjectName)
        && !string.IsNullOrWhiteSpace(SeriesName)
        && !string.IsNullOrWhiteSpace(ProjectFolder);

    private bool CanCancelOperation() => IsBusy
        && _operationCancellation is { IsCancellationRequested: false };

    private void BrowseProjectFolder()
    {
        if (!CanBeginOperation())
        {
            return;
        }

        string? selectedFolder = PickFolder("Choose an empty folder for the new project");
        if (selectedFolder is not null)
        {
            ProjectFolder = selectedFolder;
        }
    }

    private Task CreateProjectAsync()
    {
        if (!CanCreateProject())
        {
            return Task.CompletedTask;
        }

        var request = new CreateProjectRequest(ProjectName, SeriesName, ProjectFolder);
        return RunOperationAsync(
            token => _projectService.CreateAsync(request, token),
            "Creating project...",
            "Project created and opened.");
    }

    private async Task OpenProjectAsync()
    {
        if (!CanBeginOperation())
        {
            return;
        }

        string? selectedFolder = PickFolder("Open an INKLUME project folder");
        if (selectedFolder is null)
        {
            return;
        }

        await RunOperationAsync(
            token => _projectService.OpenAsync(selectedFolder, token),
            "Opening project...",
            "Project opened.");
    }

    private string? PickFolder(string title)
    {
        try
        {
            ErrorMessage = string.Empty;
            return _folderPicker.PickFolder(title);
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "The folder picker could not be opened. Please try again.");
            return null;
        }
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task<ProjectWorkspace>> operation,
        string pendingMessage,
        string completedMessage)
    {
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = pendingMessage;

        try
        {
            // SQLite's async APIs still perform synchronous I/O; keep that work off the dispatcher.
            ProjectWorkspace workspace = await Task.Run(
                () => operation(cancellation.Token), cancellation.Token);
            CurrentWorkspace = workspace;
            StatusMessage = completedMessage;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Operation cancelled. Any files already created were preserved.";
        }
        catch (ProjectOperationException exception)
        {
            Trace.TraceError("Project operation failed ({0}): {1}", exception.Code, exception);
            ErrorMessage = exception.Message;
            StatusMessage = "The project operation could not be completed.";
        }
        catch (ArgumentException exception)
        {
            Trace.TraceError("Project input validation failed: {0}", exception);
            ErrorMessage = exception.Message;
            StatusMessage = "Check the project details and try again.";
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "An unexpected error prevented the project operation. Please try again.");
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void CancelOperation()
    {
        if (!CanCancelOperation())
        {
            return;
        }

        StatusMessage = "Cancelling the project operation...";
        _operationCancellation?.Cancel();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private void ReportUnexpectedError(Exception exception, string message)
    {
        Trace.TraceError("Desktop project command failed: {0}", exception);
        ErrorMessage = message;
        StatusMessage = "The project operation could not be completed.";
    }
}
