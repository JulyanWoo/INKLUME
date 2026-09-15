using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;

namespace Inklume.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ProjectService _projectService;
    private readonly ChapterService _chapterService;
    private readonly ILocalChapterSourceProvider _localChapterSourceProvider;
    private readonly IProjectDialogService _dialogService;
    private readonly IApplicationDialogService _applicationDialogService;
    private readonly TextRegionService _textRegionService;
    private readonly IPagePreviewLoader _previewLoader;
    private CancellationTokenSource? _operationCancellation;
    private bool _isClosing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeVisible))]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceVisible))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentStatusMessage))]
    [NotifyPropertyChangedFor(nameof(CurrentProgressMessage))]
    [NotifyPropertyChangedFor(nameof(CurrentWarningMessage))]
    [NotifyPropertyChangedFor(nameof(CurrentErrorMessage))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsCancellationAvailable))]
    [NotifyCanExecuteChangedFor(nameof(CloseProjectCommand))]
    private WorkspaceViewModel? _workspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsCancellationAvailable))]
    [NotifyPropertyChangedFor(nameof(CurrentStatusMessage))]
    [NotifyPropertyChangedFor(nameof(HasStatusDetails))]
    [NotifyCanExecuteChangedFor(nameof(NewProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRecentProjectCommand))]
    private bool _isShellBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStatusMessage))]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CurrentStatusMessage))]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    [NotifyPropertyChangedFor(nameof(HasStatusDetails))]
    private string _errorMessage = string.Empty;

    public MainViewModel(
        ProjectService projectService,
        ChapterService chapterService,
        ILocalChapterSourceProvider localChapterSourceProvider,
        IProjectDialogService dialogService,
        IApplicationDialogService applicationDialogService,
        TextRegionService textRegionService,
        IPagePreviewLoader previewLoader)
    {
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(chapterService);
        ArgumentNullException.ThrowIfNull(localChapterSourceProvider);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(applicationDialogService);
        ArgumentNullException.ThrowIfNull(textRegionService);
        ArgumentNullException.ThrowIfNull(previewLoader);
        _projectService = projectService;
        _chapterService = chapterService;
        _localChapterSourceProvider = localChapterSourceProvider;
        _dialogService = dialogService;
        _applicationDialogService = applicationDialogService;
        _textRegionService = textRegionService;
        _previewLoader = previewLoader;

        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync, CanBeginProjectOperation);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync, CanBeginProjectOperation);
        OpenRecentProjectCommand = new AsyncRelayCommand<RecentProjectViewModel>(OpenRecentProjectAsync, CanOpenRecentProject);
        CloseProjectCommand = new AsyncRelayCommand(CloseProjectAsync, () => Workspace is not null);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsCancellationAvailable);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = string.Empty);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? ExitRequested;

    public string ApplicationName { get; } = "INKLUME";

    public string Subtitle { get; } = "AI Comic Localization Studio";

    public string WindowTitle => Workspace is null ? ApplicationName : $"{Workspace.ProjectName} - {ApplicationName}";

    public bool IsHomeVisible => Workspace is null;

    public bool IsWorkspaceVisible => Workspace is not null;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsBusy => IsShellBusy || Workspace?.IsBusy == true;

    public bool IsCancellationAvailable => (_operationCancellation is { IsCancellationRequested: false })
        || Workspace?.IsCancellationAvailable == true;

    public string CurrentStatusMessage => !string.IsNullOrEmpty(CurrentErrorMessage)
        ? CurrentErrorMessage
        : (IsShellBusy ? StatusMessage : Workspace?.StatusMessage ?? StatusMessage);

    public string CurrentProgressMessage => Workspace?.ProgressMessage ?? string.Empty;

    public string CurrentWarningMessage => Workspace?.WarningMessage ?? string.Empty;

    public string CurrentErrorMessage => Workspace?.ErrorMessage ?? ErrorMessage;

    public bool HasStatusDetails => IsBusy
        || !string.IsNullOrEmpty(CurrentProgressMessage)
        || !string.IsNullOrEmpty(CurrentWarningMessage)
        || !string.IsNullOrEmpty(CurrentErrorMessage);

    public ObservableCollection<RecentProjectViewModel> RecentProjects { get; } = [];

    public IAsyncRelayCommand NewProjectCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IAsyncRelayCommand OpenFolderCommand => OpenProjectCommand;

    public IAsyncRelayCommand<RecentProjectViewModel> OpenRecentProjectCommand { get; }

    public IAsyncRelayCommand CloseProjectCommand { get; }

    public IAsyncRelayCommand CloseFolderCommand => CloseProjectCommand;

    public IRelayCommand CancelOperationCommand { get; }

    public IRelayCommand DismissErrorCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand ShowAboutCommand { get; }

    public IRelayCommand ExitCommand { get; }

    public async Task StopAsync()
    {
        _isClosing = true;
        NotifyCommandStates();
        CancelOperation();
        if (Workspace is { } workspace)
        {
            await workspace.StopAsync();
        }

        await Task.WhenAll(
            NewProjectCommand.ExecutionTask ?? Task.CompletedTask,
            OpenProjectCommand.ExecutionTask ?? Task.CompletedTask,
            OpenRecentProjectCommand.ExecutionTask ?? Task.CompletedTask,
            CloseProjectCommand.ExecutionTask ?? Task.CompletedTask);
    }

    partial void OnWorkspaceChanging(WorkspaceViewModel? value)
    {
        if (Workspace is not null)
        {
            Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }
    }

    partial void OnWorkspaceChanged(WorkspaceViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += OnWorkspacePropertyChanged;
        }

        NotifyShellStateChanged();
    }

    private bool CanBeginProjectOperation() => !_isClosing && !IsShellBusy && Workspace is null;

    private bool CanOpenRecentProject(RecentProjectViewModel? project)
        => project is not null && CanBeginProjectOperation();

    private async Task NewProjectAsync()
    {
        CreateProjectRequest? request;
        try
        {
            ErrorMessage = string.Empty;
            request = _dialogService.ShowNewProjectDialog();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "The new project dialog could not be opened. Please try again.");
            return;
        }

        if (request is null)
        {
            return;
        }

        await RunProjectOperationAsync(
            token => _projectService.CreateAsync(request, token),
            "Creating project...",
            "Project created.");
    }

    private async Task OpenProjectAsync()
    {
        string? selectedFolder;
        try
        {
            ErrorMessage = string.Empty;
            selectedFolder = _dialogService.PickProjectFolder();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "The project folder picker could not be opened. Please try again.");
            return;
        }

        if (selectedFolder is null)
        {
            return;
        }

        await RunProjectOperationAsync(
            token => _projectService.OpenAsync(selectedFolder, token),
            "Opening project...",
            "Project opened.");
    }

    private Task OpenRecentProjectAsync(RecentProjectViewModel? project)
    {
        if (project is null)
        {
            return Task.CompletedTask;
        }

        return RunProjectOperationAsync(
            token => _projectService.OpenAsync(project.SourceRoot, token),
            "Opening folder...",
            "Folder opened.");
    }

    private async Task RunProjectOperationAsync(
        Func<CancellationToken, Task<ProjectWorkspace>> operation,
        string pendingMessage,
        string completedMessage)
    {
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsShellBusy = true;
        StatusMessage = pendingMessage;
        ErrorMessage = string.Empty;
        NotifyShellStateChanged();

        try
        {
            ProjectWorkspace projectWorkspace = await Task.Run(
                () => operation(cancellation.Token), cancellation.Token);
            var workspace = new WorkspaceViewModel(
                projectWorkspace,
                _chapterService,
                _localChapterSourceProvider,
                _dialogService,
                _textRegionService,
                _previewLoader);
            await workspace.InitializeAsync(cancellation.Token);
            AddRecentProject(projectWorkspace);
            Workspace = workspace;
            StatusMessage = completedMessage;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Operation cancelled.";
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
            IsShellBusy = false;
            NotifyShellStateChanged();
        }
    }

    private async Task CloseProjectAsync()
    {
        WorkspaceViewModel? workspace = Workspace;
        if (workspace is null)
        {
            return;
        }

        StatusMessage = "Closing project...";
        await workspace.StopAsync();
        Workspace = null;
        ErrorMessage = string.Empty;
        StatusMessage = "Project closed.";
    }

    private void CancelOperation()
    {
        bool hadCancelableOperation = IsCancellationAvailable;
        _operationCancellation?.Cancel();
        Workspace?.CancelOperationCommand.Execute(null);
        if (hadCancelableOperation)
        {
            StatusMessage = "Cancelling operation...";
        }

        NotifyShellStateChanged();
    }

    private void OpenSettings()
    {
        try
        {
            _applicationDialogService.ShowSettingsDialog();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "Settings could not be opened. Please try again.");
        }
    }

    private void ShowAbout()
    {
        try
        {
            _applicationDialogService.ShowAboutDialog();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "About INKLUME could not be opened. Please try again.");
        }
    }

    private void AddRecentProject(ProjectWorkspace workspace)
    {
        RecentProjectViewModel? existing = RecentProjects.FirstOrDefault(project => project.ProjectId == workspace.Project.Id);
        if (existing is not null)
        {
            RecentProjects.Remove(existing);
        }

        RecentProjects.Insert(0, RecentProjectViewModel.FromWorkspace(workspace, DateTimeOffset.Now));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.StatusMessage)
            or nameof(WorkspaceViewModel.ProgressMessage)
            or nameof(WorkspaceViewModel.WarningMessage)
            or nameof(WorkspaceViewModel.ErrorMessage)
            or nameof(WorkspaceViewModel.IsBusy)
            or nameof(WorkspaceViewModel.IsCancellationAvailable))
        {
            NotifyShellStateChanged();
        }
    }

    private void NotifyShellStateChanged()
    {
        OnPropertyChanged(nameof(IsHomeVisible));
        OnPropertyChanged(nameof(IsWorkspaceVisible));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsCancellationAvailable));
        OnPropertyChanged(nameof(CurrentStatusMessage));
        OnPropertyChanged(nameof(CurrentProgressMessage));
        OnPropertyChanged(nameof(CurrentWarningMessage));
        OnPropertyChanged(nameof(CurrentErrorMessage));
        OnPropertyChanged(nameof(HasStatusDetails));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        NewProjectCommand.NotifyCanExecuteChanged();
        OpenProjectCommand.NotifyCanExecuteChanged();
        OpenRecentProjectCommand.NotifyCanExecuteChanged();
        CloseProjectCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private void ReportUnexpectedError(Exception exception, string message)
    {
        Trace.TraceError("Desktop shell command failed: {0}", exception);
        ErrorMessage = message;
        StatusMessage = "The project operation could not be completed.";
    }
}
