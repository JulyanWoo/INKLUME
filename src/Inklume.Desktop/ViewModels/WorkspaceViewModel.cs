using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly ChapterService _chapterService;
    private readonly ILocalChapterSourceProvider _localChapterSourceProvider;
    private readonly IProjectDialogService _dialogService;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _selectionCancellation;
    private Task _selectionTask = Task.CompletedTask;
    private ProjectExplorerNode? _selectedExplorerNode;
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
    [NotifyPropertyChangedFor(nameof(SelectedChapterNumber))]
    private Chapter? _selectedChapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPage))]
    [NotifyPropertyChangedFor(nameof(SelectedPageNumber))]
    [NotifyPropertyChangedFor(nameof(SelectedOriginalFileName))]
    [NotifyPropertyChangedFor(nameof(SelectedInternalFileName))]
    [NotifyPropertyChangedFor(nameof(SelectedRelativePath))]
    [NotifyPropertyChangedFor(nameof(SelectedContentHash))]
    [NotifyPropertyChangedFor(nameof(SelectedPixelDimensions))]
    private PageWorkspace? _selectedPage;

    [ObservableProperty]
    private string _statusMessage = "Project loaded.";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _warningMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ProjectExplorerNode? SelectedExplorerNode
    {
        get => _selectedExplorerNode;
        private set
        {
            if (ReferenceEquals(_selectedExplorerNode, value))
            {
                return;
            }

            if (_selectedExplorerNode is not null)
            {
                _selectedExplorerNode.IsSelected = false;
            }

            _selectedExplorerNode = value;

            if (_selectedExplorerNode is not null)
            {
                _selectedExplorerNode.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _isOutputVisible;

    public WorkspaceViewModel(
        ProjectWorkspace workspace,
        ChapterService chapterService,
        ILocalChapterSourceProvider localChapterSourceProvider,
        IProjectDialogService dialogService,
        TextRegionService textRegionService,
        IPagePreviewLoader previewLoader)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapterService);
        ArgumentNullException.ThrowIfNull(localChapterSourceProvider);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(textRegionService);
        ArgumentNullException.ThrowIfNull(previewLoader);
        _workspace = workspace;
        _chapterService = chapterService;
        _localChapterSourceProvider = localChapterSourceProvider;
        _dialogService = dialogService;
        VisualEditor = new VisualEditorViewModel(
            workspace, chapterService, textRegionService, previewLoader);
        VisualEditor.PropertyChanged += OnVisualEditorPropertyChanged;

        ImportChapterCommand = new AsyncRelayCommand(ImportChapterAsync, () => IsIdle);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsCancellationAvailable);
        ToggleOutputCommand = new RelayCommand(() => IsOutputVisible = !IsOutputVisible);
    }

    public string ProjectName => Workspace.Project.Name;

    public string SeriesName => Workspace.Project.SeriesName;

    public string ProjectPath => Workspace.RootPath;

    public bool IsIdle => !IsBusy && !_isStopping;

    public bool IsCancellationAvailable => (_operationCancellation is { IsCancellationRequested: false })
        || (_selectionCancellation is { IsCancellationRequested: false });

    public bool HasSelectedPage => SelectedPage is not null;

    public bool HasChapters => ExplorerRoots
        .SelectMany(node => node.Children)
        .Any(node => node.Kind == ExplorerNodeKind.Chapters && node.Children.Count > 0);

    public string SelectedChapterNumber => SelectedChapter?.Number.ToString() ?? string.Empty;

    public string SelectedPageNumber => SelectedPage?.Page.Number.ToString() ?? string.Empty;

    public string SelectedOriginalFileName => SelectedPage?.Page.OriginalFileName ?? string.Empty;

    public string SelectedInternalFileName => SelectedPage is null
        ? string.Empty
        : Path.GetFileName(SelectedPage.Page.RelativePath);

    public string SelectedRelativePath => SelectedPage?.Page.RelativePath ?? string.Empty;

    public string SelectedContentHash => SelectedPage?.Page.ContentHash ?? string.Empty;

    public string SelectedPixelDimensions => SelectedPage?.Page is { PixelWidth: { } width, PixelHeight: { } height }
        ? $"{width} x {height}"
        : "Not resolved";

    public ObservableCollection<ProjectExplorerNode> ExplorerRoots { get; } = [];

    public ObservableCollection<string> OutputMessages { get; } = [];

    public IAsyncRelayCommand ImportChapterCommand { get; }

    public IRelayCommand CancelOperationCommand { get; }

    public IRelayCommand ToggleOutputCommand { get; }

    public VisualEditorViewModel VisualEditor { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ReloadExplorerAsync(selectedChapterId: null, cancellationToken);
        AddOutput("Project opened.");
        StatusMessage = "Project loaded.";
    }

    public Task SelectExplorerNodeAsync(ProjectExplorerNode? node)
    {
        if (node is null || ReferenceEquals(node, SelectedExplorerNode) || _isStopping)
        {
            return Task.CompletedTask;
        }

        SelectedExplorerNode = node;
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
        VisualEditor.Clear();
        ClearSelection();
        ExplorerRoots.Clear();
        OnPropertyChanged(nameof(HasChapters));
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
        OnPropertyChanged(nameof(HasChapters));

        ProjectExplorerNode? selectedChapterNode = selectedChapterId.HasValue
            ? chaptersNode.Children.FirstOrDefault(node => node.Chapter?.Id == selectedChapterId.Value)
            : chaptersNode.Children.FirstOrDefault();
        if (selectedChapterNode is not null)
        {
            await PopulateChapterPagesAsync(selectedChapterNode, cancellationToken);
            selectedChapterNode.IsExpanded = true;
            ProjectExplorerNode? firstPage = selectedChapterNode.Children.FirstOrDefault();
            if (firstPage is not null)
            {
                SelectedExplorerNode = firstPage;
                await LoadPageAsync(firstPage, cancellationToken);
            }
            else
            {
                SelectedExplorerNode = selectedChapterNode;
                SelectChapterMetadata(selectedChapterNode);
            }
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
                    await SelectChapterNodeAsync(node, cancellation.Token);
                    break;
                case ExplorerNodeKind.Page:
                    await LoadPageAsync(node, cancellation.Token);
                    break;
                default:
                    ClearSelectionData();
                    StatusMessage = "Project loaded.";
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_selectionCancellation, cancellation))
            {
                StatusMessage = "Selection loading cancelled.";
            }
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

    private async Task SelectChapterNodeAsync(ProjectExplorerNode node, CancellationToken cancellationToken)
    {
        SelectChapterMetadata(node);
        if (node.Children.Count == 0)
        {
            await PopulateChapterPagesAsync(node, cancellationToken);
        }

        node.IsExpanded = true;
    }

    private void SelectChapterMetadata(ProjectExplorerNode node)
    {
        Chapter chapter = node.Chapter
            ?? throw new InvalidOperationException("The explorer chapter node has no chapter metadata.");
        StatusMessage = $"Chapter {chapter.Number} selected.";
        SelectedChapter = chapter;
        SelectedPage = null;
        VisualEditor.Clear();
    }

    private async Task PopulateChapterPagesAsync(ProjectExplorerNode node, CancellationToken cancellationToken)
    {
        Chapter chapter = node.Chapter
            ?? throw new InvalidOperationException("The explorer chapter node has no chapter metadata.");
        IReadOnlyList<PageWorkspace> pages = await Task.Run(
            () => _chapterService.GetPagesAsync(Workspace, chapter.Id, cancellationToken), cancellationToken);
        node.Children.Clear();
        foreach (PageWorkspace page in pages)
        {
            node.Children.Add(ProjectExplorerNode.CreatePage(chapter, page));
        }
    }

    private async Task LoadPageAsync(ProjectExplorerNode node, CancellationToken cancellationToken)
    {
        PageWorkspace page = node.Page
            ?? throw new InvalidOperationException("The explorer page node has no page metadata.");
        SelectedChapter = node.Chapter;
        SelectedPage = page;
        StatusMessage = $"Loading page {page.Page.Number}...";
        try
        {
            SelectedPage = await VisualEditor.LoadPageAsync(page, cancellationToken);
            StatusMessage = VisualEditor.StatusMessage;
            AddOutput($"Page {page.Page.Number} loaded from chapter {node.Chapter?.Number}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Loading page preview failed: {0}", exception);
            VisualEditor.ReportInteractionError(exception);
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
        SelectedExplorerNode = null;
        ClearSelectionData();
    }

    private void ClearSelectionData()
    {
        SelectedChapter = null;
        SelectedPage = null;
        VisualEditor.Clear();
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

    private void OnVisualEditorPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(VisualEditorViewModel.StatusMessage)
            && !string.IsNullOrWhiteSpace(VisualEditor.StatusMessage))
        {
            StatusMessage = VisualEditor.StatusMessage;
        }

        if (eventArgs.PropertyName == nameof(VisualEditorViewModel.ErrorMessage)
            && !string.IsNullOrWhiteSpace(VisualEditor.ErrorMessage))
        {
            ErrorMessage = VisualEditor.ErrorMessage;
        }
    }
}
