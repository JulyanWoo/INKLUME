using System.IO;
using System.Windows.Media;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.VisualEditor;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class GenericImageAndCandidateTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("image.png", ExplorerNodeKind.GenericImageFile)]
    [InlineData("scan.jpg", ExplorerNodeKind.GenericImageFile)]
    [InlineData("photo.jpeg", ExplorerNodeKind.GenericImageFile)]
    [InlineData("page.webp", ExplorerNodeKind.GenericImageFile)]
    [InlineData("PHOTO.PNG", ExplorerNodeKind.GenericImageFile)]
    [InlineData("PHOTO.JPG", ExplorerNodeKind.GenericImageFile)]
    [InlineData("notes.txt", ExplorerNodeKind.GenericFile)]
    [InlineData("document.pdf", ExplorerNodeKind.GenericFile)]
    [InlineData("archive.zip", ExplorerNodeKind.GenericFile)]
    public void FileMapping_ShouldMapSupportedImagesToGenericImageFile_AndOthersToGenericFile(
        string fileName, ExplorerNodeKind expectedKind)
    {
        ProjectExplorerNode node = SupportedImageFormats.IsSupported(fileName)
            ? ProjectExplorerNode.CreateGenericImageFile(fileName, $@"C:\Project\{fileName}")
            : ProjectExplorerNode.CreateGenericFile(fileName, $@"C:\Project\{fileName}");

        Assert.Equal(expectedKind, node.Kind);
        Assert.Equal(fileName, node.DisplayName);
        Assert.Equal($@"C:\Project\{fileName}", node.FullPath);
    }

    [Fact]
    public async Task GenericImageSelection_ShouldLoadReadOnlyPreview_WithoutPageOrTextRegions()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        var previewLoader = new MockPreviewLoader();
        var visualEditor = new VisualEditorViewModel(
            workspace,
            new ChapterService(new MockChapterStore(), TimeProvider.System),
            new TextRegionService(new TextRegionStoreStub(), TimeProvider.System),
            previewLoader);

        await visualEditor.LoadGenericImageAsync(@"C:\Project\chapters\loose1.png", CancellationToken.None);

        Assert.True(visualEditor.IsGenericPreview);
        Assert.False(visualEditor.IsEditingEnabled);
        Assert.Null(visualEditor.SelectedPage);
        Assert.Empty(visualEditor.TextRegions);
        Assert.Null(visualEditor.SelectedRegion);
        Assert.True(visualEditor.HasPreview);
        Assert.Equal(@"C:\Project\chapters\loose1.png", previewLoader.LastLoadedImagePath);
        Assert.False(visualEditor.RectangleToolCommand.CanExecute(null));
        Assert.False(visualEditor.PolygonToolCommand.CanExecute(null));
        Assert.False(visualEditor.DeleteSelectedRegionCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanonicalPageSelection_ShouldEnableEditingTools_AndSupportRegions()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var previewLoader = new MockPreviewLoader();
        var visualEditor = new VisualEditorViewModel(
            workspace,
            new ChapterService(new MockChapterStore(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            previewLoader);

        await visualEditor.LoadPageAsync(page, CancellationToken.None);

        Assert.False(visualEditor.IsGenericPreview);
        Assert.True(visualEditor.IsEditingEnabled);
        Assert.NotNull(visualEditor.SelectedPage);
        Assert.True(visualEditor.RectangleToolCommand.CanExecute(null));
        Assert.True(visualEditor.PolygonToolCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingBetweenPageAndGenericImage_ShouldSafelyTransitionModesAndClearRegionState()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var service = new TextRegionService(regionStore, TimeProvider.System);
        var previewLoader = new MockPreviewLoader();
        var visualEditor = new VisualEditorViewModel(
            workspace,
            new ChapterService(new MockChapterStore(page), TimeProvider.System),
            service,
            previewLoader);

        // 1. Load canonical page and create region
        await visualEditor.LoadPageAsync(page, CancellationToken.None);
        TextRegion region = await visualEditor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(50, 50), CancellationToken.None)
            ?? throw new InvalidOperationException();
        visualEditor.SelectedRegion = region;
        Assert.NotNull(visualEditor.SelectedRegion);
        Assert.True(visualEditor.IsEditingEnabled);

        // 2. Switch to GenericImage: region state cleared, tools disabled
        await visualEditor.LoadGenericImageAsync(@"C:\Project\loose.jpg", CancellationToken.None);
        Assert.True(visualEditor.IsGenericPreview);
        Assert.False(visualEditor.IsEditingEnabled);
        Assert.Null(visualEditor.SelectedPage);
        Assert.Null(visualEditor.SelectedRegion);
        Assert.Empty(visualEditor.TextRegions);

        // 3. Switch back to Page: editing restored, regions reloaded
        await visualEditor.LoadPageAsync(page, CancellationToken.None);
        Assert.False(visualEditor.IsGenericPreview);
        Assert.True(visualEditor.IsEditingEnabled);
        Assert.NotNull(visualEditor.SelectedPage);
        Assert.Single(visualEditor.TextRegions);
    }

    [Fact]
    public async Task CandidateDetection_ShouldRecognizeCandidateFolder_AndIgnoreCanonicalChapter()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inklume-cand-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string chaptersDir = Path.Combine(tempDir, "chapters");
            string candidateDir = Path.Combine(chaptersDir, "001");
            string canonicalDir = Path.Combine(chaptersDir, "002");
            string otherDir = Path.Combine(tempDir, "references");
            Directory.CreateDirectory(candidateDir);
            Directory.CreateDirectory(canonicalDir);
            Directory.CreateDirectory(otherDir);

            // Add images
            File.WriteAllBytes(Path.Combine(candidateDir, "01.png"), [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllBytes(Path.Combine(canonicalDir, "01.png"), [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllBytes(Path.Combine(otherDir, "ref.png"), [0x89, 0x50, 0x4E, 0x47]);

            var project = new TranslationProject(Guid.NewGuid(), "Test", "Test", Timestamp, Timestamp);
            var workspace = new ProjectWorkspace(project, tempDir, Path.Combine(tempDir, "data"));
            var chapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(2), "Chapter 2", Timestamp, Timestamp);

            var explorer = new ProjectExplorerViewModel(
                workspace,
                new ChapterService(new MockChapterStore(chapter), TimeProvider.System));

            await explorer.InitializeAsync(CancellationToken.None);
            ProjectExplorerNode root = Assert.Single(explorer.Roots);
            ProjectExplorerNode chaptersNode = root.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
            await chaptersNode.EnsureLoadedAsync();

            // Candidate folder 001 should be GenericFolder with IsChapterCandidate = true
            ProjectExplorerNode candidateNode = Assert.Single(chaptersNode.Children, c => c.DisplayName == "001");
            Assert.Equal(ExplorerNodeKind.GenericFolder, candidateNode.Kind);
            Assert.True(candidateNode.IsChapterCandidate);

            // Canonical chapter should be Chapter kind, NOT candidate
            ProjectExplorerNode canonicalNode = Assert.Single(chaptersNode.Children, c => c.DisplayName == "Chapter 2");
            Assert.Equal(ExplorerNodeKind.Chapter, canonicalNode.Kind);
            Assert.False(canonicalNode.IsChapterCandidate);

            // Outside chapters/ should not be candidate
            ProjectExplorerNode refNode = Assert.Single(root.Children, c => c.DisplayName == "references");
            Assert.False(refNode.IsChapterCandidate);
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
    public async Task CandidateImport_ShouldPrefillSourceFolder_AndReuseCanonicalFlow()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inklume-import-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string chaptersDir = Path.Combine(tempDir, "chapters");
            string candidateDir = Path.Combine(chaptersDir, "001");
            Directory.CreateDirectory(candidateDir);
            File.WriteAllBytes(Path.Combine(candidateDir, "01.png"), [0x89, 0x50, 0x4E, 0x47]);

            var project = new TranslationProject(Guid.NewGuid(), "Test", "Test", Timestamp, Timestamp);
            var workspace = new ProjectWorkspace(project, tempDir, Path.Combine(tempDir, "data"));
            var dialogService = new MockDialogService
            {
                ImportChapterResult = new ImportChapterDialogResult(new ChapterNumber(1), "Chapter 1", candidateDir)
            };

            var chapterStore = new MockChapterStore();
            var chapterService = new ChapterService(chapterStore, TimeProvider.System);
            var regionStore = new TextRegionStoreStub();
            var regionService = new TextRegionService(regionStore, TimeProvider.System);
            var sourceProvider = new MockSourceProvider();

            var workspaceVm = new WorkspaceViewModel(
                workspace,
                chapterService,
                sourceProvider,
                dialogService,
                regionService,
                new MockPreviewLoader());

            await workspaceVm.InitializeAsync(CancellationToken.None);

            // Execute ImportChapterWithFolderCommand with candidateDir
            workspaceVm.ImportChapterWithFolderCommand.Execute(candidateDir);
            await (workspaceVm.ImportChapterWithFolderCommand.ExecutionTask ?? Task.CompletedTask);

            // Verify dialog was opened with prefilled candidate folder
            Assert.Equal(candidateDir, dialogService.LastInitialSourceFolder);
            // Verify canonical import was invoked
            Assert.True(chapterStore.ImportCalled);
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
    public async Task InspectorProperties_ShouldExposeRealMetadataForGenericImage_AndClearWhenDeselected()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Test", "Test", Timestamp, Timestamp);
        var workspace = new ProjectWorkspace(project, @"C:\Project", @"C:\Project_data");
        var previewLoader = new MockPreviewLoader();
        var workspaceVm = new WorkspaceViewModel(
            workspace,
            new ChapterService(new MockChapterStore(), TimeProvider.System),
            new MockSourceProvider(),
            new MockDialogService(),
            new TextRegionService(new TextRegionStoreStub(), TimeProvider.System),
            previewLoader);

        var node = ProjectExplorerNode.CreateGenericImageFile("loose.png", @"C:\Project\chapters\loose.png");

        // Select generic image node
        await workspaceVm.SelectExplorerNodeAsync(node);

        Assert.True(workspaceVm.HasSelectedGenericImage);
        Assert.Equal("loose.png", workspaceVm.SelectedGenericImageFileName);
        Assert.Equal(".png", workspaceVm.SelectedGenericImageExtension);

        // Select non-generic node (e.g. chapters folder)
        var chaptersFolderNode = ProjectExplorerNode.CreateChaptersFolder(@"C:\Project\chapters");
        await workspaceVm.SelectExplorerNodeAsync(chaptersFolderNode);
        Assert.False(workspaceVm.HasSelectedGenericImage);
    }

    private static ProjectWorkspace CreateWorkspace()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Test Project", "Test Series", Timestamp, Timestamp);
        return new ProjectWorkspace(project, @"C:\Project", @"C:\Project_data");
    }

    private static PageWorkspace CreatePage(int number)
    {
        var page = new Page(
            Guid.NewGuid(),
            Guid.NewGuid(),
            number,
            $"{number:D2}.png",
            $"chapters/001/{number:D2}.png",
            new string('A', 64),
            800,
            1200);

        return new PageWorkspace(page, $@"C:\Project\chapters\001\{number:D2}.png");
    }

    private sealed class MockPreviewLoader : IPagePreviewLoader
    {
        public string? LastLoadedImagePath { get; private set; }

        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, rawPixelWidth, rawPixelHeight));

        public Task<PagePreview> LoadImageAsync(string filePath, CancellationToken cancellationToken)
        {
            LastLoadedImagePath = filePath;
            return Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, 1080, 1920));
        }
    }

    private sealed class MockChapterStore : IChapterStore
    {
        private readonly List<Chapter> _chapters = [];
        private readonly List<PageWorkspace> _pages = [];

        public bool ImportCalled { get; private set; }

        public MockChapterStore()
        {
        }

        public MockChapterStore(params PageWorkspace[] pages)
        {
            _pages.AddRange(pages);
        }

        public MockChapterStore(params Chapter[] chapters)
        {
            _chapters.AddRange(chapters);
        }

        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            ImportCalled = true;
            _chapters.Add(chapter);
            return Task.FromResult(new ChapterImportResult(workspace, chapter, [], []));
        }

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterFolderPath,
            ChapterNumber chapterNumber,
            string? title,
            CancellationToken cancellationToken)
        {
            var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _chapters.Add(chapter);
            return Task.FromResult(new ChapterIndexResult(workspace, chapter, []));
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>(_chapters);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>(_pages.Where(p => p.Page.ChapterId == chapterId).ToArray());

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(_pages.Single(p => p.Page.Id == pageId));
    }

    private sealed class MockDialogService : IProjectDialogService
    {
        public ImportChapterDialogResult? ImportChapterResult { get; set; }
        public string? LastInitialSourceFolder { get; private set; }

        public CreateProjectRequest? ShowNewProjectDialog() => null;

        public ImportChapterDialogResult? ShowImportChapterDialog(string? initialSourceFolder = null)
        {
            LastInitialSourceFolder = initialSourceFolder;
            return ImportChapterResult;
        }

        public string? PickProjectFolder() => null;
    }

    private sealed class MockSourceProvider : ILocalChapterSourceProvider
    {
        public Task<ChapterSource> LoadAsync(
            string sourceFolder, string projectRoot, CancellationToken cancellationToken)
        {
            var image = new ChapterSourceImage("01.png", ".png", _ => ValueTask.FromResult<Stream>(new MemoryStream([0x89, 0x50, 0x4E, 0x47])));
            return Task.FromResult(new ChapterSource([image], []));
        }
    }
}
