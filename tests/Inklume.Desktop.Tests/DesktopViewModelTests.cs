using System.IO;
using System.Windows.Media;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class DesktopViewModelTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewProjectCommand_ShouldNavigateToWorkspaceAndAddRecentProject_WhenCreationSucceeds()
    {
        var projectStore = new ProjectStoreStub();
        var dialogs = new DialogServiceStub
        {
            NewProjectResult = new CreateProjectRequest("Demo Project", "Demo Series", @"C:\Demo Project")
        };
        MainViewModel viewModel = CreateMainViewModel(projectStore, new ChapterStoreStub(), dialogs);

        viewModel.NewProjectCommand.Execute(null);
        await (viewModel.NewProjectCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.True(viewModel.IsWorkspaceVisible);
        Assert.False(viewModel.IsHomeVisible);
        Assert.Equal("Demo Project", viewModel.Workspace?.ProjectName);
        RecentProjectViewModel recent = Assert.Single(viewModel.RecentProjects);
        Assert.Equal("Demo Series", recent.SeriesName);
    }

    [Fact]
    public async Task OpenProjectCommand_ShouldNavigateToWorkspace_WhenFolderIsSelected()
    {
        ProjectWorkspace workspace = CreateWorkspace("Opened Project", @"C:\Opened Project");
        var projectStore = new ProjectStoreStub { WorkspaceToOpen = workspace };
        var dialogs = new DialogServiceStub { ProjectFolderResult = workspace.SourceRoot };
        MainViewModel viewModel = CreateMainViewModel(projectStore, new ChapterStoreStub(), dialogs);

        viewModel.OpenProjectCommand.Execute(null);
        await (viewModel.OpenProjectCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(workspace.Project.Id, viewModel.Workspace?.Workspace.Project.Id);
        Assert.True(viewModel.CloseProjectCommand.CanExecute(null));
        Assert.False(viewModel.NewProjectCommand.CanExecute(null));
        Assert.False(viewModel.OpenProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task CloseProjectCommand_ShouldReturnHomeAndClearWorkspaceSelection()
    {
        ProjectWorkspace workspace = CreateWorkspace("Closable Project", @"C:\Closable Project");
        Chapter chapter = CreateChapter(workspace.Project.Id);
        PageWorkspace page = CreatePage(chapter.Id, 1, "page 1.png", "001.png");
        var projectStore = new ProjectStoreStub { WorkspaceToOpen = workspace };
        var chapterStore = new ChapterStoreStub([chapter], [page]);
        var dialogs = new DialogServiceStub { ProjectFolderResult = workspace.SourceRoot };
        MainViewModel viewModel = CreateMainViewModel(projectStore, chapterStore, dialogs);
        viewModel.OpenProjectCommand.Execute(null);
        await (viewModel.OpenProjectCommand.ExecutionTask ?? Task.CompletedTask);

        ProjectExplorerNode projectNode = Assert.Single(viewModel.Workspace!.ExplorerRoots);
        ProjectExplorerNode chaptersNode = projectNode.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = chaptersNode.Children.First(c => c.Kind == ExplorerNodeKind.Chapter);
        await chapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = chapterNode.Children.First(c => c.Kind == ExplorerNodeKind.RawFolder);
        await rawFolder.EnsureLoadedAsync();
        ProjectExplorerNode pageNode = rawFolder.Children.First(c => c.Kind == ExplorerNodeKind.Page);
        await viewModel.Workspace.SelectExplorerNodeAsync(pageNode);
        Assert.NotNull(viewModel.Workspace?.SelectedPage);

        viewModel.CloseProjectCommand.Execute(null);
        await (viewModel.CloseProjectCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.True(viewModel.IsHomeVisible);
        Assert.Null(viewModel.Workspace);
        Assert.False(viewModel.CloseProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeAsync_ShouldBuildExplorerAndExposeSelectedPageMetadata()
    {
        ProjectWorkspace workspace = CreateWorkspace("Explorer Project", @"C:\Explorer Project");
        Chapter chapter = CreateChapter(workspace.Project.Id, 10.5m, "Interlude");
        PageWorkspace firstPage = CreatePage(chapter.Id, 1, "página uno.png", "001.png");
        PageWorkspace secondPage = CreatePage(chapter.Id, 2, "page 2.jpg", "002.jpg");
        var previewLoader = new PreviewLoaderStub();
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(
            workspace, new ChapterStoreStub([chapter], [firstPage, secondPage]), previewLoader: previewLoader);

        await viewModel.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode projectNode = Assert.Single(viewModel.ExplorerRoots);
        Assert.Contains(projectNode.Children, n => n.Kind == ExplorerNodeKind.ChaptersFolder);
        Assert.Contains(projectNode.Children, n => n.Kind == ExplorerNodeKind.ContextFolder);
        Assert.True(viewModel.HasChapters);

        ProjectExplorerNode chaptersNode = projectNode.Children.First(n => n.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = Assert.Single(chaptersNode.Children);
        Assert.Equal(ExplorerNodeKind.Chapter, chapterNode.Kind);

        await chapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = Assert.Single(chapterNode.Children);
        Assert.Equal(ExplorerNodeKind.RawFolder, rawFolder.Kind);

        await rawFolder.EnsureLoadedAsync();
        Assert.Equal(2, rawFolder.Children.Count);

        ProjectExplorerNode firstPageNode = rawFolder.Children[0];
        await viewModel.SelectExplorerNodeAsync(firstPageNode);

        Assert.True(viewModel.HasSelectedPage);
        Assert.Equal("10.5", viewModel.SelectedChapterNumber);
        Assert.Equal("1", viewModel.SelectedPageNumber);
        Assert.Equal("página uno.png", viewModel.SelectedOriginalFileName);
        Assert.Equal("001.png", viewModel.SelectedInternalFileName);
        Assert.Equal(firstPage.Page.RelativePath, viewModel.SelectedRelativePath);
        Assert.Equal(firstPage.Page.ContentHash, viewModel.SelectedContentHash);
        Assert.Equal(firstPage.FilePath, previewLoader.LastFilePath);
    }

    [Fact]
    public async Task SelectExplorerNodeAsync_ShouldLoadOnlyTheSelectedPage()
    {
        ProjectWorkspace workspace = CreateWorkspace("Selection Project", @"C:\Selection Project");
        Chapter chapter = CreateChapter(workspace.Project.Id);
        PageWorkspace firstPage = CreatePage(chapter.Id, 1, "one.png", "001.png");
        PageWorkspace secondPage = CreatePage(chapter.Id, 2, "two.png", "002.jpg");
        var previewLoader = new PreviewLoaderStub();
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(
            workspace, new ChapterStoreStub([chapter], [firstPage, secondPage]), previewLoader: previewLoader);
        await viewModel.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode chaptersNode = viewModel.ExplorerRoots[0].Children.First(n => n.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = chaptersNode.Children.First(n => n.Kind == ExplorerNodeKind.Chapter);
        await chapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = chapterNode.Children.First(n => n.Kind == ExplorerNodeKind.RawFolder);
        await rawFolder.EnsureLoadedAsync();
        ProjectExplorerNode secondPageNode = rawFolder.Children[1];

        await viewModel.SelectExplorerNodeAsync(secondPageNode);

        Assert.Equal(2, viewModel.SelectedPage?.Page.Number);
        Assert.Equal(secondPage.FilePath, previewLoader.LastFilePath);
        Assert.Equal(1, viewModel.VisualEditor.ZoomScale);
    }

    [Fact]
    public async Task SelectExplorerNodeAsync_WhenSelectingChapter_ShouldSelectOnlyChapterAndClearPageSelection()
    {
        ProjectWorkspace workspace = CreateWorkspace("Chapter Selection Project", @"C:\Chapter Selection Project");
        Chapter chapter = CreateChapter(workspace.Project.Id);
        PageWorkspace firstPage = CreatePage(chapter.Id, 1, "one.png", "001.png");
        PageWorkspace secondPage = CreatePage(chapter.Id, 2, "two.png", "002.png");
        var previewLoader = new PreviewLoaderStub();
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(
            workspace, new ChapterStoreStub([chapter], [firstPage, secondPage]), previewLoader: previewLoader);
        await viewModel.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode projectNode = viewModel.ExplorerRoots[0];
        ProjectExplorerNode chaptersNode = projectNode.Children.First(n => n.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = chaptersNode.Children.First(n => n.Kind == ExplorerNodeKind.Chapter);
        await chapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = chapterNode.Children.First(n => n.Kind == ExplorerNodeKind.RawFolder);
        await rawFolder.EnsureLoadedAsync();
        ProjectExplorerNode firstPageNode = rawFolder.Children[0];

        await viewModel.SelectExplorerNodeAsync(firstPageNode);

        Assert.False(chapterNode.IsSelected);
        Assert.True(firstPageNode.IsSelected);
        Assert.True(viewModel.HasSelectedPage);

        await viewModel.SelectExplorerNodeAsync(chapterNode);

        Assert.True(chapterNode.IsSelected);
        Assert.False(firstPageNode.IsSelected);
        Assert.False(projectNode.IsSelected);
        Assert.False(chaptersNode.IsSelected);
        Assert.False(viewModel.HasSelectedPage);
        Assert.Null(viewModel.SelectedPage);
        Assert.Null(viewModel.VisualEditor.PreviewImage);
    }

    [Fact]
    public async Task SelectExplorerNodeAsync_WhenSelectingPage_ShouldEnsureOnlyThatPageIsSelected()
    {
        ProjectWorkspace workspace = CreateWorkspace("Page Selection Project", @"C:\Page Selection Project");
        Chapter chapter = CreateChapter(workspace.Project.Id);
        PageWorkspace firstPage = CreatePage(chapter.Id, 1, "one.png", "001.png");
        PageWorkspace secondPage = CreatePage(chapter.Id, 2, "two.png", "002.png");
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(
            workspace, new ChapterStoreStub([chapter], [firstPage, secondPage]));
        await viewModel.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode projectNode = viewModel.ExplorerRoots[0];
        ProjectExplorerNode chaptersNode = projectNode.Children.First(n => n.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = chaptersNode.Children.First(n => n.Kind == ExplorerNodeKind.Chapter);
        await chapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = chapterNode.Children.First(n => n.Kind == ExplorerNodeKind.RawFolder);
        await rawFolder.EnsureLoadedAsync();
        ProjectExplorerNode firstPageNode = rawFolder.Children[0];
        ProjectExplorerNode secondPageNode = rawFolder.Children[1];

        await viewModel.SelectExplorerNodeAsync(firstPageNode);
        Assert.True(firstPageNode.IsSelected);

        await viewModel.SelectExplorerNodeAsync(secondPageNode);

        Assert.True(secondPageNode.IsSelected);
        Assert.False(firstPageNode.IsSelected);
        Assert.False(chapterNode.IsSelected);
        Assert.False(chaptersNode.IsSelected);
        Assert.False(projectNode.IsSelected);
        Assert.Equal(2, viewModel.SelectedPage?.Page.Number);
    }

    [Fact]
    public void ToggleOutputCommand_ShouldOpenAndCloseOutputPanel()
    {
        ProjectWorkspace workspace = CreateWorkspace("Output Project", @"C:\Output Project");
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(workspace, new ChapterStoreStub());

        Assert.False(viewModel.IsOutputVisible);
        viewModel.ToggleOutputCommand.Execute(null);
        Assert.True(viewModel.IsOutputVisible);
        viewModel.ToggleOutputCommand.Execute(null);
        Assert.False(viewModel.IsOutputVisible);
    }

    [Fact]
    public void ApplicationDialogCommands_ShouldUseTheSharedDialogService()
    {
        var applicationDialogs = new ApplicationDialogServiceStub();
        MainViewModel viewModel = CreateMainViewModel(
            new ProjectStoreStub(),
            new ChapterStoreStub(),
            new DialogServiceStub(),
            applicationDialogs);

        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, applicationDialogs.SettingsOpenCount);
        Assert.Equal(1, applicationDialogs.AboutOpenCount);
    }

    [Fact]
    public async Task CancelOperationCommand_ShouldBecomeAvailableAndCancelAnImport()
    {
        ProjectWorkspace workspace = CreateWorkspace("Cancel Project", @"C:\Cancel Project");
        var sourceProvider = new BlockingSourceProvider();
        var dialogs = new DialogServiceStub
        {
            ImportChapterResult = new ImportChapterDialogResult(new ChapterNumber(1), null, @"C:\Source")
        };
        WorkspaceViewModel viewModel = CreateWorkspaceViewModel(
            workspace, new ChapterStoreStub(), dialogs, sourceProvider);

        viewModel.ImportChapterCommand.Execute(null);
        await sourceProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsCancellationAvailable);
        Assert.True(viewModel.CancelOperationCommand.CanExecute(null));
        viewModel.CancelOperationCommand.Execute(null);
        await (viewModel.ImportChapterCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.False(viewModel.IsCancellationAvailable);
        Assert.Equal("Chapter import cancelled.", viewModel.StatusMessage);
    }

    [Fact]
    public void NewProjectDialogViewModel_ShouldRequireAllFieldsBeforeCreate()
    {
        var viewModel = new NewProjectDialogViewModel(new FolderPickerStub());

        Assert.False(viewModel.CreateCommand.CanExecute(null));
        viewModel.ProjectName = "Project";
        viewModel.SeriesName = "Series";
        viewModel.ProjectFolder = @"C:\Project";

        Assert.True(viewModel.CreateCommand.CanExecute(null));
        viewModel.CreateCommand.Execute(null);
        Assert.Equal("Project", viewModel.Result?.Name);
    }

    [Fact]
    public void NewProjectDialogViewModel_ShouldAcceptNonEmptyFolderAndCompleteDialog()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "inklume_test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "existing.txt"), "dummy");
            bool closeRequested = false;
            var viewModel = new NewProjectDialogViewModel(new FolderPickerStub());
            viewModel.CloseRequested += (_, _) => closeRequested = true;

            viewModel.ProjectName = "Test Project";
            viewModel.SeriesName = "Test Series";
            viewModel.ProjectFolder = tempDir;

            Assert.True(viewModel.CreateCommand.CanExecute(null));
            viewModel.CreateCommand.Execute(null);

            Assert.NotNull(viewModel.Result);
            Assert.True(closeRequested);
            Assert.Empty(viewModel.ErrorMessage);
            Assert.Equal(tempDir, viewModel.Result.SourceRoot);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void NewProjectDialogViewModel_ShouldRespectExactFolderWhenBrowseSelectsOccupiedFolder()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "inklume_test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "existing.txt"), "dummy");
            var picker = new FolderPickerStub(tempDir);
            var viewModel = new NewProjectDialogViewModel(picker)
            {
                ProjectName = "Solo Leveling"
            };

            viewModel.BrowseCommand.Execute(null);

            Assert.Equal(tempDir, viewModel.ProjectFolder);
            Assert.Empty(viewModel.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void MainViewModel_ShouldExposeHasErrorMessageAndAllowDismiss()
    {
        MainViewModel viewModel = CreateMainViewModel(
            new ProjectStoreStub(),
            new ChapterStoreStub(),
            new DialogServiceStub());

        Assert.False(viewModel.HasErrorMessage);
        Assert.Empty(viewModel.ErrorMessage);

        viewModel.ErrorMessage = "Failed to load project.";
        Assert.True(viewModel.HasErrorMessage);
        Assert.Equal("Failed to load project.", viewModel.CurrentStatusMessage);

        viewModel.DismissErrorCommand.Execute(null);
        Assert.False(viewModel.HasErrorMessage);
        Assert.Empty(viewModel.ErrorMessage);
    }

    [Fact]
    public void ImportChapterDialogViewModel_ShouldRejectInvalidNumberAndAcceptDecimalNumber()
    {
        var viewModel = new ImportChapterDialogViewModel(new FolderPickerStub())
        {
            SourceFolder = @"C:\Source",
            ChapterNumber = "invalid"
        };

        viewModel.ImportCommand.Execute(null);
        Assert.Null(viewModel.Result);
        Assert.NotEmpty(viewModel.ErrorMessage);

        viewModel.ChapterNumber = "10.5";
        viewModel.ImportCommand.Execute(null);
        Assert.Equal(new ChapterNumber(10.5m), viewModel.Result?.Number);
    }

    private static MainViewModel CreateMainViewModel(
        IProjectStore projectStore,
        IChapterStore chapterStore,
        DialogServiceStub dialogs,
        ApplicationDialogServiceStub? applicationDialogs = null)
        => new(
            new ProjectService(projectStore, TimeProvider.System),
            new ChapterService(chapterStore, TimeProvider.System),
            new EmptySourceProvider(),
            dialogs,
            applicationDialogs ?? new ApplicationDialogServiceStub(),
            CreateTextRegionService(chapterStore),
            new PreviewLoaderStub());

    private static WorkspaceViewModel CreateWorkspaceViewModel(
        ProjectWorkspace workspace,
        IChapterStore chapterStore,
        DialogServiceStub? dialogs = null,
        ILocalChapterSourceProvider? sourceProvider = null,
        IPagePreviewLoader? previewLoader = null)
    {
        return new WorkspaceViewModel(
            workspace,
            new ChapterService(chapterStore, TimeProvider.System),
            sourceProvider ?? new EmptySourceProvider(),
            dialogs ?? new DialogServiceStub(),
            CreateTextRegionService(chapterStore),
            previewLoader ?? new PreviewLoaderStub());
    }

    private static TextRegionService CreateTextRegionService(IChapterStore chapterStore)
    {
        var store = new TextRegionStoreStub();
        if (chapterStore is ChapterStoreStub stub)
        {
            foreach (PageWorkspace page in stub.Pages)
            {
                store.AddPage(page.Page.HasDimensions ? page.Page : page.Page.WithDimensions(1080, 1920));
            }
        }

        return new TextRegionService(store, TimeProvider.System);
    }

    private static ProjectWorkspace CreateWorkspace(string name, string rootPath)
        => new(new TranslationProject(Guid.NewGuid(), name, $"{name} Series", Timestamp, Timestamp), rootPath, $"{rootPath}_data");

    private static Chapter CreateChapter(Guid projectId, decimal number = 1, string? title = null)
        => new(Guid.NewGuid(), projectId, new ChapterNumber(number), title, Timestamp, Timestamp);

    private static PageWorkspace CreatePage(
        Guid chapterId,
        int number,
        string originalFileName,
        string internalFileName)
    {
        string relativePath = Path.Combine("chapters", "001", "001_raw", internalFileName);
        var page = new Page(
            Guid.NewGuid(), chapterId, number, originalFileName, relativePath, new string('A', 64), 1080, 1920);
        return new PageWorkspace(page, Path.Combine(@"C:\Project", relativePath));
    }

    private sealed class ProjectStoreStub : IProjectStore
    {
        public ProjectWorkspace? WorkspaceToOpen { get; init; }

        public Task<ProjectWorkspace> CreateAsync(
            TranslationProject project,
            string rootPath,
            CancellationToken cancellationToken)
            => Task.FromResult(new ProjectWorkspace(project, rootPath, $"{rootPath}_data"));

        public Task<ProjectWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken)
            => Task.FromResult(WorkspaceToOpen ?? throw new InvalidOperationException("No project was configured."));
    }
}

internal sealed class ChapterStoreStub(
    IReadOnlyList<Chapter>? chapters = null,
    IReadOnlyList<PageWorkspace>? pages = null) : IChapterStore
{
    public IReadOnlyList<PageWorkspace> Pages { get; } = pages ?? [];

    public Task<ChapterImportResult> ImportAsync(
        ProjectWorkspace workspace,
        Chapter chapter,
        ChapterSource source,
        IProgress<ChapterImportProgress>? progress,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
        => Task.FromResult(chapters ?? (IReadOnlyList<Chapter>)[]);

    public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
        ProjectWorkspace workspace,
        Guid chapterId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PageWorkspace>>([.. Pages.Where(p => p.Page.ChapterId == chapterId)]);

    public Task<PageWorkspace> EnsurePageDimensionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        PageWorkspace page = Pages.Single(item => item.Page.Id == pageId);
        return Task.FromResult(page.Page.HasDimensions
            ? page
            : page with { Page = page.Page.WithDimensions(1080, 1920) });
    }

    public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
        ProjectWorkspace workspace,
        string chapterDirectory,
        ChapterNumber chapterNumber,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, now, now);
        var matchedPages = Pages.Where(p => p.Page.ChapterId == chapter.Id).ToList();
        return Task.FromResult(new ChapterIndexResult(workspace, chapter, matchedPages));
    }
}

internal sealed class DialogServiceStub : IProjectDialogService
{
    public CreateProjectRequest? NewProjectResult { get; init; }

    public ImportChapterDialogResult? ImportChapterResult { get; init; }

    public string? ProjectFolderResult { get; init; }

    public CreateProjectRequest? ShowNewProjectDialog() => NewProjectResult;

    public string? LastInitialSourceFolder { get; private set; }

    public ImportChapterDialogResult? ShowImportChapterDialog(string? initialSourceFolder = null)
    {
        LastInitialSourceFolder = initialSourceFolder;
        return ImportChapterResult;
    }

    public string? PickProjectFolder() => ProjectFolderResult;
}

internal sealed class ApplicationDialogServiceStub : IApplicationDialogService
{
    public int SettingsOpenCount { get; private set; }

    public int AboutOpenCount { get; private set; }

    public void ShowSettingsDialog() => SettingsOpenCount++;

    public void ShowAboutDialog() => AboutOpenCount++;
}

internal sealed class EmptySourceProvider : ILocalChapterSourceProvider
{
    public Task<ChapterSource> LoadAsync(
        string sourceFolder,
        string projectRoot,
        CancellationToken cancellationToken)
        => Task.FromResult(new ChapterSource([], []));
}

internal sealed class BlockingSourceProvider : ILocalChapterSourceProvider
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ChapterSource> LoadAsync(
        string sourceFolder,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new ChapterSource([], []);
    }
}

internal sealed class PreviewLoaderStub : IPagePreviewLoader
{
    public string? LastFilePath { get; private set; }

    public Task<PagePreview> LoadAsync(
        string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
    {
        LastFilePath = filePath;
        return Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, rawPixelWidth, rawPixelHeight));
    }

    public Task<PagePreview> LoadImageAsync(string filePath, CancellationToken cancellationToken)
    {
        LastFilePath = filePath;
        return Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, 1080, 1920));
    }
}

internal sealed class FolderPickerStub(string? selectedFolder = null) : IProjectFolderPicker
{
    public string? SelectedFolder { get; set; } = selectedFolder;

    public string? PickFolder(string description) => SelectedFolder;
}
