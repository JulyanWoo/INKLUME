using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Desktop.Services;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly ChapterService _chapterService;
    private readonly ILocalChapterSourceProvider _localChapterSourceProvider;
    private readonly IProjectDialogService _dialogService;
    private readonly IPagePreviewLoader _previewLoader;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _selectionCancellation;
    private Task _selectionTask = Task.CompletedTask;
    private ProjectExplorerNode? _activeNode;
    private bool _isStopping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    [NotifyPropertyChangedFor(nameof(SeriesName))]
    [NotifyPropertyChangedFor(nameof(ProjectPath))]
    private ProjectWorkspace _workspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsCancellationAvailable))]
    [NotifyCanExecuteChangedFor(nameof(ImportChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private Chapter? _selectedChapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPage))]
    private PageWorkspace? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string _previewErrorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Project loaded.";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _warningMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    private int _zoomPercent = 100;

    public WorkspaceViewModel(
        ProjectWorkspace workspace,
        ChapterService chapterService,
        ILocalChapterSourceProvider localChapterSourceProvider,
        IProjectDialogService dialogService,
        IPagePreviewLoader previewLoader)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapterService);
        ArgumentNullException.ThrowIfNull(localChapterSourceProvider);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(previewLoader);
        _workspace = workspace;
        _chapterService = chapterService;
        _localChapterSourceProvider = localChapterSourceProvider;
        _dialogService = dialogService;
        _previewLoader = previewLoader;

        ImportChapterCommand = new AsyncRelayCommand(ImportChapterAsync, () => IsIdle);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsCancellationAvailable);
        ZoomInCommand = new RelayCommand(() => ZoomPercent += 10, () => ZoomPercent < 200);
        ZoomOutCommand = new RelayCommand(() => ZoomPercent -= 10, () => ZoomPercent > 30);
        ResetZoomCommand = new RelayCommand(() => ZoomPercent = 100);
    }

    public string ProjectName => Workspace.Project.Name;

    public string SeriesName => Workspace.Project.SeriesName;

    public string ProjectPath => Workspace.RootPath;

    public bool IsIdle => !IsBusy && !_isStopping;

    public bool IsCancellationAvailable => (_operationCancellation is { IsCancellationRequested: false })
        || (_selectionCancellation is { IsCancellationRequested: false });

    public bool HasSelectedPage => SelectedPage is not null;

    public bool HasPreview => PreviewImage is not null;

    public double ZoomScale => ZoomPercent / 100d;

    public ObservableCollection<ProjectExplorerNode> ExplorerRoots { get; } = [];

    public ObservableCollection<string> OutputMessages { get; } = [];

    public IAsyncRelayCommand ImportChapterCommand { get; }

    public IRelayCommand CancelOperationCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

    public IRelayCommand ResetZoomCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ReloadExplorerAsync(selectedChapterId: null, cancellationToken);
        AddOutput("Project opened.");
        StatusMessage = "Project loaded.";
    }

    public Task SelectExplorerNodeAsync(ProjectExplorerNode? node)
    {
        if (node is null || ReferenceEquals(node, _activeNode) || _isStopping)
        {
            return Task.CompletedTask;
        }

        _activeNode = node;
        _selectionTask = SelectExplorerNodeCoreAsync(node);
        return _selectionTask;
    }

    public async Task StopAsync()
    {
        _isStopping = true;
        OnPropertyChanged(nameof(IsIdle));
        ImportChapterCommand.NotifyCanExecuteChanged();
        CancelOperation();
        await CancelSelectionAsync();
        await Task.WhenAll(ImportChapterCommand.ExecutionTask ?? Task.CompletedTask, _selectionTask);
        ClearSelection();
        ExplorerRoots.Clear();
    }

    partial void OnZoomPercentChanged(int value)
    {
        OnPropertyChanged(nameof(ZoomScale));
    }

    private async Task ImportChapterAsync()
    {
        ImportChapterDialogResult? dialogResult;
        try
        {
            dialogResult = _dialogService.ShowImportChapterDialog();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "The import dialog could not be opened. Please try again.");
            return;
        }

        if (dialogResult is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsBusy = true;
        NotifyCancellationChanged();
        ClearMessages();
        ProgressMessage = "Preparing chapter import...";
        StatusMessage = "Importing chapter...";
        AddOutput($"Importing chapter {dialogResult.Number}.");
        var progress = new Progress<ChapterImportProgress>(value =>
        {
            int current = Math.Min(value.Completed + 1, value.Total);
            ProgressMessage = value.Completed == value.Total
                ? $"Imported {value.Total} of {value.Total} pages."
                : $"Importing {current} / {value.Total}: {value.CurrentFile}";
            StatusMessage = ProgressMessage;
        });

        try
        {
            ChapterSource source = await Task.Run(
                () => _localChapterSourceProvider.LoadAsync(
                    dialogResult.SourceFolder, Workspace.RootPath, cancellation.Token),
                cancellation.Token);
            var request = new ImportChapterRequest(dialogResult.Number, dialogResult.Title, source);
            ChapterImportResult result = await Task.Run(
                () => _chapterService.ImportAsync(Workspace, request, progress, cancellation.Token),
                cancellation.Token);
            Workspace = result.Workspace;
            await ReloadExplorerAsync(result.Chapter.Id, cancellation.Token);
            WarningMessage = result.IgnoredFiles.Count == 0
                ? string.Empty
                : $"Ignored {result.IgnoredFiles.Count} unsupported file(s): {string.Join(", ", result.IgnoredFiles)}";
            StatusMessage = $"Chapter {result.Chapter.Number} imported.";
            ProgressMessage = string.Empty;
            AddOutput($"Chapter {result.Chapter.Number} imported with {result.Pages.Count} page(s).");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Chapter import cancelled.";
            ProgressMessage = string.Empty;
            AddOutput("Operation cancelled. Original images were preserved.");
        }
        catch (ProjectOperationException exception)
        {
            ReportProjectError(exception);
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception, "An unexpected error prevented the chapter import. Please try again.");
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
            NotifyCancellationChanged();
        }
    }

    private async Task ReloadExplorerAsync(Guid? selectedChapterId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Chapter> chapters = await Task.Run(
            () => _chapterService.GetChaptersAsync(Workspace, cancellationToken), cancellationToken);
        ClearSelection();
        ExplorerRoots.Clear();
        ProjectExplorerNode projectNode = ProjectExplorerNode.CreateProject(Workspace);
        ProjectExplorerNode chaptersNode = ProjectExplorerNode.CreateChaptersGroup();
        foreach (Chapter chapter in chapters)
        {
            chaptersNode.Children.Add(ProjectExplorerNode.CreateChapter(chapter));
        }

        projectNode.Children.Add(chaptersNode);
        ExplorerRoots.Add(projectNode);

        ProjectExplorerNode? selectedNode = selectedChapterId.HasValue
            ? chaptersNode.Children.FirstOrDefault(node => node.Chapter?.Id == selectedChapterId.Value)
            : chaptersNode.Children.FirstOrDefault();
        if (selectedNode is not null)
        {
            selectedNode.IsSelected = true;
            _activeNode = selectedNode;
            await LoadChapterAsync(selectedNode, cancellationToken);
        }
    }

    private async Task SelectExplorerNodeCoreAsync(ProjectExplorerNode node)
    {
        await CancelSelectionAsync();
        using var cancellation = new CancellationTokenSource();
        _selectionCancellation = cancellation;
        NotifyCancellationChanged();
        try
        {
            switch (node.Kind)
            {
                case ExplorerNodeKind.Chapter:
                    await LoadChapterAsync(node, cancellation.Token);
                    break;
                case ExplorerNodeKind.Page:
                    await LoadPageAsync(node, cancellation.Token);
                    break;
                default:
                    ClearSelection();
                    StatusMessage = "Project loaded.";
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Selection loading cancelled.";
        }
        catch (Exception exception)
        {
            Trace.TraceError("Workspace selection failed: {0}", exception);
            ErrorMessage = exception is ProjectOperationException projectError
                ? projectError.Message
                : "The selected item could not be loaded.";
            StatusMessage = "Selection could not be loaded.";
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cancellation))
            {
                _selectionCancellation = null;
                NotifyCancellationChanged();
            }
        }
    }

    private async Task LoadChapterAsync(ProjectExplorerNode node, CancellationToken cancellationToken)
    {
        Chapter chapter = node.Chapter
            ?? throw new InvalidOperationException("The explorer chapter node has no chapter metadata.");
        StatusMessage = $"Loading chapter {chapter.Number}...";
        SelectedChapter = chapter;
        SelectedPage = null;
        PreviewImage = null;
        PreviewErrorMessage = string.Empty;
        IReadOnlyList<PageWorkspace> pages = await Task.Run(
            () => _chapterService.GetPagesAsync(Workspace, chapter.Id, cancellationToken), cancellationToken);
        node.Children.Clear();
        foreach (PageWorkspace page in pages)
        {
            node.Children.Add(ProjectExplorerNode.CreatePage(chapter, page));
        }

        node.IsExpanded = true;
        ProjectExplorerNode? firstPage = node.Children.FirstOrDefault();
        if (firstPage is not null)
        {
            _activeNode = firstPage;
            firstPage.IsSelected = true;
            await LoadPageAsync(firstPage, cancellationToken);
        }
        else
        {
            StatusMessage = $"Chapter {chapter.Number} has no pages.";
        }
    }

    private async Task LoadPageAsync(ProjectExplorerNode node, CancellationToken cancellationToken)
    {
        PageWorkspace page = node.Page
            ?? throw new InvalidOperationException("The explorer page node has no page metadata.");
        SelectedChapter = node.Chapter;
        SelectedPage = page;
        PreviewImage = null;
        PreviewErrorMessage = string.Empty;
        ZoomPercent = 100;
        StatusMessage = $"Loading page {page.Page.Number}...";
        try
        {
            PreviewImage = await _previewLoader.LoadAsync(page.FilePath, cancellationToken);
            StatusMessage = $"Page {page.Page.Number} loaded.";
            AddOutput($"Page {page.Page.Number} loaded from chapter {node.Chapter?.Number}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Loading page preview failed: {0}", exception);
            PreviewErrorMessage = "This page could not be previewed. The imported file was not modified.";
            StatusMessage = "Page preview unavailable.";
        }
    }

    private async Task CancelSelectionAsync()
    {
        CancellationTokenSource? cancellation = _selectionCancellation;
        if (cancellation is not null)
        {
            await cancellation.CancelAsync();
        }
    }

    private void CancelOperation()
    {
        bool hadCancelableOperation = IsCancellationAvailable;
        _operationCancellation?.Cancel();
        _selectionCancellation?.Cancel();
        if (hadCancelableOperation)
        {
            StatusMessage = "Cancelling operation...";
        }

        NotifyCancellationChanged();
    }

    private void ClearSelection()
    {
        _activeNode = null;
        SelectedChapter = null;
        SelectedPage = null;
        PreviewImage = null;
        PreviewErrorMessage = string.Empty;
        ZoomPercent = 100;
    }

    private void ClearMessages()
    {
        ErrorMessage = string.Empty;
        WarningMessage = string.Empty;
    }

    private void AddOutput(string message)
    {
        OutputMessages.Add($"{DateTimeOffset.Now:HH:mm:ss}  {message}");
    }

    private void NotifyCancellationChanged()
    {
        OnPropertyChanged(nameof(IsCancellationAvailable));
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    private void ReportProjectError(ProjectOperationException exception)
    {
        Trace.TraceError("Project operation failed ({0}): {1}", exception.Code, exception);
        ErrorMessage = exception.Message;
        StatusMessage = "The project operation could not be completed.";
        ProgressMessage = string.Empty;
        AddOutput($"Project operation failed: {exception.Message}");
    }

    private void ReportUnexpectedError(Exception exception, string message)
    {
        Trace.TraceError("Workspace command failed: {0}", exception);
        ErrorMessage = message;
        StatusMessage = "The project operation could not be completed.";
        ProgressMessage = string.Empty;
        AddOutput(message);
    }
}
