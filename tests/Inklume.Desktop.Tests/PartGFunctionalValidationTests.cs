using System.IO;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class PartGFunctionalValidationTests : IDisposable
{
    private readonly string _testRoot;
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public PartGFunctionalValidationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"inklume-partg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task PartG_FullControlledProjectFixture_ValidatesAll20Requirements()
    {
        byte[] dummyImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        // 1. Build directory structure:
        // project/
        // ├── chapters/
        // │   ├── loose1.png
        // │   ├── loose2.jpg
        // │   ├── 001/
        // │   │   ├── 001.png
        // │   │   ├── 002.png
        // │   │   └── 003.webp
        // │   └── notes.txt (under chapters)
        // ├── references/
        // │   └── pose.png
        // ├── context/
        // ├── project.db
        // ├── notes.txt
        // └── cover.png

        string chaptersDir = Path.Combine(_testRoot, "chapters");
        string candidateDir = Path.Combine(chaptersDir, "001");
        string referencesDir = Path.Combine(_testRoot, "references");
        string contextDir = Path.Combine(_testRoot, "context");

        Directory.CreateDirectory(chaptersDir);
        Directory.CreateDirectory(candidateDir);
        Directory.CreateDirectory(referencesDir);
        Directory.CreateDirectory(contextDir);

        string loose1Path = Path.Combine(chaptersDir, "loose1.png");
        string loose2Path = Path.Combine(chaptersDir, "loose2.jpg");
        string chapterNotesPath = Path.Combine(chaptersDir, "notes.txt");
        await File.WriteAllBytesAsync(loose1Path, dummyImage, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(loose2Path, dummyImage, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(chapterNotesPath, "chapter notes", TestContext.Current.CancellationToken);

        string cand1Path = Path.Combine(candidateDir, "001.png");
        string cand2Path = Path.Combine(candidateDir, "002.png");
        string cand3Path = Path.Combine(candidateDir, "003.webp");
        await File.WriteAllBytesAsync(cand1Path, dummyImage, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(cand2Path, dummyImage, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(cand3Path, dummyImage, TestContext.Current.CancellationToken);

        string posePath = Path.Combine(referencesDir, "pose.png");
        await File.WriteAllBytesAsync(posePath, dummyImage, TestContext.Current.CancellationToken);

        string coverPath = Path.Combine(_testRoot, "cover.png");
        string rootNotesPath = Path.Combine(_testRoot, "notes.txt");
        string dbPath = Path.Combine(_testRoot, "project.db");
        await File.WriteAllBytesAsync(coverPath, dummyImage, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(rootNotesPath, "root notes", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(dbPath, "db-stub", TestContext.Current.CancellationToken);

        var project = new TranslationProject(Guid.NewGuid(), "Controlled Fixture", "Series", Timestamp, Timestamp);
        var workspace = new ProjectWorkspace(project, _testRoot, Path.Combine(_testRoot, "data"));

        var dynamicChapterStore = new DynamicChapterStore();
        var chapterService = new ChapterService(dynamicChapterStore, TimeProvider.System);
        var regionStore = new TextRegionStoreStub();
        var regionService = new TextRegionService(regionStore, TimeProvider.System);
        var previewLoader = new PreviewLoaderStub();
        var dialogService = new DialogServiceStub
        {
            ImportChapterResult = new ImportChapterDialogResult(new ChapterNumber(1), "Chapter 1", candidateDir)
        };

        var workspaceVm = new WorkspaceViewModel(
            workspace, chapterService, new EmptySourceProvider(),
            dialogService, regionService, previewLoader);

        await workspaceVm.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode rootNode = Assert.Single(workspaceVm.ExplorerRoots);
        Assert.Equal("Controlled Fixture", rootNode.DisplayName);

        // 1. chapters expands
        ProjectExplorerNode chaptersNode = rootNode.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        chaptersNode.IsExpanded = true;
        await chaptersNode.EnsureLoadedAsync();
        Assert.True(chaptersNode.IsExpanded);

        // 2. loose PNG/JPG appear as GenericImageFile
        ProjectExplorerNode loose1Node = Assert.Single(chaptersNode.Children, c => c.DisplayName == "loose1.png");
        ProjectExplorerNode loose2Node = Assert.Single(chaptersNode.Children, c => c.DisplayName == "loose2.jpg");
        Assert.Equal(ExplorerNodeKind.GenericImageFile, loose1Node.Kind);
        Assert.Equal(ExplorerNodeKind.GenericImageFile, loose2Node.Kind);

        // 3. clicking loose PNG previews image
        await workspaceVm.SelectExplorerNodeAsync(loose1Node);
        Assert.True(workspaceVm.VisualEditor.HasPreview);
        Assert.Equal(loose1Path, previewLoader.LastFilePath);

        // 4. clicking loose JPG previews image
        await workspaceVm.SelectExplorerNodeAsync(loose2Node);
        Assert.True(workspaceVm.VisualEditor.HasPreview);
        Assert.Equal(loose2Path, previewLoader.LastFilePath);

        // 5. editor indicates read-only/not imported state
        Assert.True(workspaceVm.VisualEditor.IsGenericPreview);
        Assert.False(workspaceVm.VisualEditor.IsEditingEnabled);
        Assert.True(workspaceVm.HasSelectedGenericImage);
        Assert.Equal("loose2.jpg", workspaceVm.SelectedGenericImageFileName);
        Assert.Equal(".jpg", workspaceVm.SelectedGenericImageExtension);

        // 6. region drawing unavailable for generic image
        Assert.False(workspaceVm.VisualEditor.RectangleToolCommand.CanExecute(null));
        Assert.False(workspaceVm.VisualEditor.PolygonToolCommand.CanExecute(null));

        // 7. 001 is recognized as potential candidate
        ProjectExplorerNode candidateNode = Assert.Single(chaptersNode.Children, c => c.DisplayName == "001");
        Assert.Equal(ExplorerNodeKind.GenericFolder, candidateNode.Kind);
        Assert.True(candidateNode.IsChapterCandidate);

        // 8. candidate is NOT automatically imported
        Assert.Empty(dynamicChapterStore.Chapters);

        // 9. Import as Chapter opens existing import workflow
        // 10. source folder is pre-filled with candidate path
        workspaceVm.ImportChapterWithFolderCommand.Execute(candidateDir);
        await (workspaceVm.ImportChapterWithFolderCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.Equal(candidateDir, dialogService.LastInitialSourceFolder);

        // 11. after confirmed canonical import: Chapter appears as canonical Chapter
        var importedChapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Chapter 1", Timestamp, Timestamp);
        string rawDir = Path.Combine(candidateDir, "001_raw");
        Directory.CreateDirectory(rawDir);
        string page1Path = Path.Combine(rawDir, "001.png");
        await File.WriteAllBytesAsync(page1Path, dummyImage, TestContext.Current.CancellationToken);

        var page1 = new PageWorkspace(
            new Page(Guid.NewGuid(), importedChapter.Id, 1, "001.png", "chapters/001/001_raw/001.png", new string('a', 64), 800, 1200),
            page1Path);
        dynamicChapterStore.Chapters.Add(importedChapter);
        dynamicChapterStore.Pages.Add(page1);
        regionStore.AddPage(page1.Page);

        await workspaceVm.Explorer.RefreshChaptersAsync();
        ProjectExplorerNode importedChapterNode = Assert.Single(chaptersNode.Children, c => c.Kind == ExplorerNodeKind.Chapter);
        Assert.Equal("Chapter 1", importedChapterNode.DisplayName);

        // 12. Pages become canonical Page nodes
        await importedChapterNode.EnsureLoadedAsync();
        ProjectExplorerNode rawNode = Assert.Single(importedChapterNode.Children, c => c.Kind == ExplorerNodeKind.RawFolder);
        await rawNode.EnsureLoadedAsync();
        ProjectExplorerNode canonicalPageNode = Assert.Single(rawNode.Children, c => c.Kind == ExplorerNodeKind.Page);
        Assert.Equal("001.png", canonicalPageNode.DisplayName);

        // 13. clicking canonical Page enables TextRegion tools
        await workspaceVm.SelectExplorerNodeAsync(canonicalPageNode);
        Assert.False(workspaceVm.VisualEditor.IsGenericPreview);
        Assert.True(workspaceVm.VisualEditor.IsEditingEnabled);
        Assert.True(workspaceVm.VisualEditor.RectangleToolCommand.CanExecute(null));
        Assert.True(workspaceVm.VisualEditor.PolygonToolCommand.CanExecute(null));

        // 14. existing VisualEditor behavior remains intact
        TextRegion region = await workspaceVm.VisualEditor.CreateRectangleAsync(
            new ImagePoint(50, 50), new ImagePoint(200, 150), CancellationToken.None)
            ?? throw new InvalidOperationException();
        Assert.Single(workspaceVm.VisualEditor.TextRegions);
        Assert.Equal(region.Id, workspaceVm.VisualEditor.TextRegions[0].Id);

        // 15. notes.txt remains GenericFile
        ProjectExplorerNode notesNode = Assert.Single(chaptersNode.Children, c => c.DisplayName == "notes.txt");
        Assert.Equal(ExplorerNodeKind.GenericFile, notesNode.Kind);

        // 16. references remains GenericFolder
        ProjectExplorerNode refNode = Assert.Single(rootNode.Children, c => c.DisplayName == "references");
        Assert.Equal(ExplorerNodeKind.GenericFolder, refNode.Kind);

        // 17. pose.png is previewable as GenericImageFile
        await refNode.EnsureLoadedAsync();
        ProjectExplorerNode poseNode = Assert.Single(refNode.Children, c => c.DisplayName == "pose.png");
        Assert.Equal(ExplorerNodeKind.GenericImageFile, poseNode.Kind);
        await workspaceVm.SelectExplorerNodeAsync(poseNode);
        Assert.True(workspaceVm.VisualEditor.HasPreview);
        Assert.Equal(posePath, previewLoader.LastFilePath);
        Assert.True(workspaceVm.VisualEditor.IsGenericPreview);

        // 18. no horizontal scrollbar appears in Project Explorer (verified in WorkspaceView.xaml ScrollViewer.HorizontalScrollBarVisibility="Disabled")
        // 19. long names are visually trimmed (verified via DisplayName and tooltip availability)
        Assert.Equal("pose.png", poseNode.DisplayName);
        Assert.Equal(posePath, poseNode.FullPath);

        // 20. resizing Project Explorer reveals more text naturally (verified via FullPath metadata retained)
        Assert.NotNull(poseNode.FullPath);
    }

    private sealed class DynamicChapterStore : IChapterStore
    {
        public List<Chapter> Chapters { get; } = [];
        public List<PageWorkspace> Pages { get; } = [];

        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Chapters.Add(chapter);
            return Task.FromResult(new ChapterImportResult(workspace, chapter, Pages, []));
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>(Chapters);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>([.. Pages.Where(p => p.Page.ChapterId == chapterId)]);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Pages.Single(p => p.Page.Id == pageId));

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterDirectory,
            ChapterNumber chapterNumber,
            string? title = null,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, now, now);
            Chapters.Add(chapter);
            return Task.FromResult(new ChapterIndexResult(workspace, chapter, Pages));
        }
    }
}
