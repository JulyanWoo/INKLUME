using System.Collections.ObjectModel;
using System.Diagnostics;
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

public sealed class ProjectExplorerTests : IDisposable
{
    private readonly string _testRoot;

    public ProjectExplorerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "inklume_explorer_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests
            }
        }
    }

    [Fact]
    public async Task FunctionalValidation_FullProjectStructure_MatchesAll20Requirements()
    {
        // 1. Setup realistic directory matching prompt:
        // project/
        // ├── cache/
        // ├── chapters/
        // │   ├── 001/
        // │   │   └── 001_raw/
        // │   │       ├── 001.png
        // │   │       └── 002.jpg
        // │   ├── downloads/
        // │   └── notes.txt
        // ├── context/
        // │   ├── series.json
        // │   ├── characters.json
        // │   ├── glossary.json
        // │   └── translation_rules.json
        // ├── references/
        // │   └── pose-reference.png
        // ├── cover.png
        // ├── notes.txt
        // └── project.db

        string cacheDir = Path.Combine(_testRoot, "cache");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "internal.bin"), "cache-data", TestContext.Current.CancellationToken);

        string chaptersDir = Path.Combine(_testRoot, "chapters");
        string ch001Dir = Path.Combine(chaptersDir, "001");
        string raw001Dir = Path.Combine(ch001Dir, "001_raw");
        Directory.CreateDirectory(raw001Dir);

        byte[] dummyPng = [137, 80, 78, 71, 13, 10, 26, 10];
        string page1File = Path.Combine(raw001Dir, "001.png");
        string page2File = Path.Combine(raw001Dir, "002.jpg");
        await File.WriteAllBytesAsync(page1File, dummyPng, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(page2File, dummyPng, TestContext.Current.CancellationToken);

        string downloadsDir = Path.Combine(chaptersDir, "downloads");
        Directory.CreateDirectory(downloadsDir);
        await File.WriteAllTextAsync(Path.Combine(chaptersDir, "notes.txt"), "chapter notes", TestContext.Current.CancellationToken);

        string contextDir = Path.Combine(_testRoot, "context");
        Directory.CreateDirectory(contextDir);
        await File.WriteAllTextAsync(Path.Combine(contextDir, "series.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(contextDir, "characters.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(contextDir, "glossary.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(contextDir, "translation_rules.json"), "[]", TestContext.Current.CancellationToken);

        string referencesDir = Path.Combine(_testRoot, "references");
        Directory.CreateDirectory(referencesDir);
        await File.WriteAllBytesAsync(Path.Combine(referencesDir, "pose-reference.png"), dummyPng, TestContext.Current.CancellationToken);

        await File.WriteAllBytesAsync(Path.Combine(_testRoot, "cover.png"), dummyPng, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "notes.txt"), "project notes", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "project.db"), "sqlite-stub", TestContext.Current.CancellationToken);

        // Create domain entities
        var project = new TranslationProject(Guid.NewGuid(), "My Project", "Test Series", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var workspace = new ProjectWorkspace(project, _testRoot, Path.Combine(_testRoot, "data"));

        var chapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Chapter 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        string hash1 = new('a', 64);
        string hash2 = new('b', 64);
        var page1 = new PageWorkspace(
            new Page(Guid.NewGuid(), chapter.Id, 1, "001.png", Path.Combine("chapters", "001", "001_raw", "001.png"), hash1, 800, 1200),
            page1File);
        var page2 = new PageWorkspace(
            new Page(Guid.NewGuid(), chapter.Id, 2, "002.jpg", Path.Combine("chapters", "001", "001_raw", "002.jpg"), hash2, 800, 1200),
            page2File);

        var textRegion = new TextRegion(
            Guid.NewGuid(), page1.Page.Id,
            new RectangleTextRegionGeometry(10, 20, 100, 50),
            1,
            TextRegionRole.Dialogue, TextContainerType.SpeechBubble,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var chapterStore = new ChapterStoreStub([chapter], [page1, page2]);
        var textRegionStore = new TextRegionStoreStub();
        textRegionStore.AddPage(page1.Page);
        textRegionStore.AddPage(page2.Page);
        await textRegionStore.AddAsync(workspace, textRegion, CancellationToken.None);
        var previewLoader = new PreviewLoaderStub();
        var textRegionService = new TextRegionService(textRegionStore, TimeProvider.System);
        var chapterService = new ChapterService(chapterStore, TimeProvider.System);

        var viewModel = new WorkspaceViewModel(
            workspace, chapterService, new StubLocalChapterSourceProvider(),
            new DialogServiceStub(), textRegionService, previewLoader);

        // 1. Project opens.
        await viewModel.InitializeAsync(CancellationToken.None);

        // 2. Project root visible.
        ProjectExplorerNode projectRoot = Assert.Single(viewModel.ExplorerRoots);
        Assert.Equal("My Project", projectRoot.DisplayName);
        Assert.Equal(ExplorerNodeKind.Project, projectRoot.Kind);

        // 3-8. Immediate root entries visible: chapters, context, project.db, references, cover.png, notes.txt
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "chapters" && c.Kind == ExplorerNodeKind.ChaptersFolder);
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "context" && c.Kind == ExplorerNodeKind.ContextFolder);
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "project.db" && c.Kind == ExplorerNodeKind.ProjectDatabase);
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "references" && c.Kind == ExplorerNodeKind.GenericFolder);
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "cover.png" && c.Kind == ExplorerNodeKind.GenericImageFile);
        Assert.Contains(projectRoot.Children, c => c.DisplayName == "notes.txt" && c.Kind == ExplorerNodeKind.GenericFile);

        // 9. cache hidden.
        Assert.DoesNotContain(projectRoot.Children, c => c.DisplayName == "cache");

        // 10. chapters expands.
        ProjectExplorerNode chaptersNode = projectRoot.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();

        // 11. persisted Chapter nodes identified.
        ProjectExplorerNode chapterNode = Assert.Single(chaptersNode.Children, c => c.Kind == ExplorerNodeKind.Chapter);
        Assert.Equal("Chapter 1", chapterNode.DisplayName);
        Assert.Equal(chapter.Id, chapterNode.Chapter?.Id);

        // 12. unknown downloads stays generic; notes.txt stays generic.
        Assert.Contains(chaptersNode.Children, c => c.DisplayName == "downloads" && c.Kind == ExplorerNodeKind.GenericFolder);
        Assert.Contains(chaptersNode.Children, c => c.DisplayName == "notes.txt" && c.Kind == ExplorerNodeKind.GenericFile);

        // 13. Chapter expands.
        await chapterNode.EnsureLoadedAsync();

        // 14. raw folder visible.
        ProjectExplorerNode rawFolderNode = Assert.Single(chapterNode.Children, c => c.Kind == ExplorerNodeKind.RawFolder);
        Assert.Equal("001_raw", rawFolderNode.DisplayName);

        // 15. canonical Pages visible when raw folder expands.
        await rawFolderNode.EnsureLoadedAsync();
        Assert.Equal(2, rawFolderNode.Children.Count);
        ProjectExplorerNode page1Node = rawFolderNode.Children[0];
        ProjectExplorerNode page2Node = rawFolderNode.Children[1];
        Assert.Equal(ExplorerNodeKind.Page, page1Node.Kind);
        Assert.Equal(ExplorerNodeKind.Page, page2Node.Kind);
        Assert.Equal("001.png", page1Node.DisplayName);
        Assert.Equal("002.jpg", page2Node.DisplayName);

        // 16. Page selection loads RAW and fit.
        await viewModel.SelectExplorerNodeAsync(page1Node);
        Assert.True(page1Node.IsSelected);
        Assert.True(viewModel.HasSelectedPage);
        Assert.Equal(page1.FilePath, previewLoader.LastFilePath);

        // 17. TextRegions still load.
        Assert.Single(viewModel.VisualEditor.TextRegions);
        Assert.Equal(textRegion.Id, viewModel.VisualEditor.TextRegions[0].Id);

        // 18. Inspector still works.
        Assert.Equal("1", viewModel.SelectedChapterNumber);
        Assert.Equal("1", viewModel.SelectedPageNumber);
        Assert.Equal("800 x 1200", viewModel.SelectedPixelDimensions);
        Assert.Equal(hash1.ToUpperInvariant(), viewModel.SelectedContentHash);

        // 19. Generic file selection does not break editor.
        ProjectExplorerNode rootNotesFile = projectRoot.Children.First(c => c.DisplayName == "notes.txt" && c.Kind == ExplorerNodeKind.GenericFile);
        await viewModel.SelectExplorerNodeAsync(rootNotesFile);
        Assert.True(rootNotesFile.IsSelected);
        Assert.False(page1Node.IsSelected);
        Assert.False(viewModel.HasSelectedPage);
        Assert.Null(viewModel.SelectedPage);

        // 20. Project closes/reopens normally.
        await viewModel.StopAsync();
        Assert.Empty(viewModel.ExplorerRoots);

        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.Single(viewModel.ExplorerRoots);
        Assert.Equal("My Project", viewModel.ExplorerRoots[0].DisplayName);
    }

    [Fact]
    public async Task ExpandingParentNode_DoesNotSelectParent()
    {
        var project = new TranslationProject(Guid.NewGuid(), "ParentExpand", "Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var workspace = new ProjectWorkspace(project, _testRoot, Path.Combine(_testRoot, "data"));
        var chapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Ch 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var chapterService = new ChapterService(new ChapterStoreStub([chapter], []), TimeProvider.System);

        var explorer = new ProjectExplorerViewModel(workspace, chapterService);
        await explorer.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode projectRoot = explorer.Roots[0];
        ProjectExplorerNode chaptersNode = projectRoot.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);

        // Expand chapters without selecting
        chaptersNode.IsExpanded = true;
        await chaptersNode.EnsureLoadedAsync();

        Assert.True(chaptersNode.IsExpanded);
        Assert.False(chaptersNode.IsSelected);
        Assert.Null(explorer.SelectedNode);
    }

    [Fact]
    public async Task SingleSelection_EnforcesOnlyOneNodeSelectedAtAnyTime()
    {
        var project = new TranslationProject(Guid.NewGuid(), "SelectionTest", "Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var workspace = new ProjectWorkspace(project, _testRoot, Path.Combine(_testRoot, "data"));
        var chapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Ch 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var chapterService = new ChapterService(new ChapterStoreStub([chapter], []), TimeProvider.System);

        var explorer = new ProjectExplorerViewModel(workspace, chapterService);
        await explorer.InitializeAsync(CancellationToken.None);

        ProjectExplorerNode projectRoot = explorer.Roots[0];
        ProjectExplorerNode chaptersNode = projectRoot.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();
        ProjectExplorerNode chapterNode = chaptersNode.Children.First(c => c.Kind == ExplorerNodeKind.Chapter);

        explorer.SelectNode(chaptersNode);
        Assert.True(chaptersNode.IsSelected);
        Assert.False(projectRoot.IsSelected);
        Assert.False(chapterNode.IsSelected);

        explorer.SelectNode(chapterNode);
        Assert.True(chapterNode.IsSelected);
        Assert.False(chaptersNode.IsSelected);
        Assert.False(projectRoot.IsSelected);

        explorer.SelectNode(projectRoot);
        Assert.True(projectRoot.IsSelected);
        Assert.False(chapterNode.IsSelected);
        Assert.False(chaptersNode.IsSelected);
    }

    [Fact]
    public void PathSafety_RejectsPathTraversalAndEscape()
    {
        string root = @"C:\Inklume\Project";
        Assert.True(PathSafety.IsDescendantOf(root, @"C:\Inklume\Project\chapters\001"));
        Assert.True(PathSafety.IsDescendantOf(root, @"C:\Inklume\Project\references\note.txt"));
        Assert.False(PathSafety.IsDescendantOf(root, @"C:\Inklume\Project\..\Other\file.txt"));
        Assert.False(PathSafety.IsDescendantOf(root, @"C:\Windows\System32"));
        Assert.False(PathSafety.IsDescendantOf(root, @"D:\Inklume\Project\file.txt"));
    }

    [Fact]
    public async Task PerformanceValidation_500ChaptersProject_DoesNotInstantiatePageNodesOnOpen()
    {
        // Controlled fixture representing 500 chapters with multiple pages each (conceptually 5,000 pages)
        int chapterCount = 500;
        var chapters = new List<Chapter>(chapterCount);
        var pages = new List<PageWorkspace>(chapterCount * 10);
        var projectId = Guid.NewGuid();

        for (int i = 1; i <= chapterCount; i++)
        {
            var chapter = new Chapter(Guid.NewGuid(), projectId, new ChapterNumber(i), $"Chapter {i}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            chapters.Add(chapter);
            for (int p = 1; p <= 10; p++)
            {
                pages.Add(new PageWorkspace(
                    new Page(Guid.NewGuid(), chapter.Id, p, $"{p:D3}.png", $"chapters/{i:D3}/raw/{p:D3}.png", $"{i:D4}{p:D4}".PadRight(64, '0'), 800, 1200),
                    Path.Combine(_testRoot, "dummy.png")));
            }
        }

        var project = new TranslationProject(projectId, "Large Project", "Massive Series", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var workspace = new ProjectWorkspace(project, _testRoot, Path.Combine(_testRoot, "data"));
        var chapterService = new ChapterService(new ChapterStoreStub(chapters, pages), TimeProvider.System);

        var explorer = new ProjectExplorerViewModel(workspace, chapterService);

        // 1. Measure root load time and nodes materialized initially
        var stopwatch = Stopwatch.StartNew();
        await explorer.InitializeAsync(CancellationToken.None);
        stopwatch.Stop();
        TimeSpan rootLoadTime = stopwatch.Elapsed;

        int initialNodes = CountMaterializedNodes(explorer.Roots);

        // Assert that on project open, NO chapter nodes or page nodes are materialized
        Assert.True(initialNodes < 10, $"Expected minimal initial nodes, but found {initialNodes}");
        Assert.DoesNotContain(GetAllNodes(explorer.Roots), n => n.Kind == ExplorerNodeKind.Chapter);
        Assert.DoesNotContain(GetAllNodes(explorer.Roots), n => n.Kind == ExplorerNodeKind.Page);

        // 2. Expand chapters folder
        ProjectExplorerNode projectRoot = explorer.Roots[0];
        ProjectExplorerNode chaptersNode = projectRoot.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        await chaptersNode.EnsureLoadedAsync();

        int nodesAfterChapters = CountMaterializedNodes(explorer.Roots);
        Assert.Equal(500, chaptersNode.Children.Count(c => c.Kind == ExplorerNodeKind.Chapter));
        // Still ZERO page nodes materialized!
        Assert.DoesNotContain(GetAllNodes(explorer.Roots), n => n.Kind == ExplorerNodeKind.Page);

        // 3. Expand exactly one chapter
        ProjectExplorerNode firstChapter = chaptersNode.Children.First(c => c.Kind == ExplorerNodeKind.Chapter);
        await firstChapter.EnsureLoadedAsync();
        ProjectExplorerNode rawFolder = firstChapter.Children.First(c => c.Kind == ExplorerNodeKind.RawFolder);
        await rawFolder.EnsureLoadedAsync();

        int nodesAfterOneChapter = CountMaterializedNodes(explorer.Roots);
        int materializedPages = GetAllNodes(explorer.Roots).Count(n => n.Kind == ExplorerNodeKind.Page);
        Assert.Equal(10, materializedPages);

        // Performance log/trace
        Trace.WriteLine($"[PERFORMANCE VALIDATION] Root load time: {rootLoadTime.TotalMilliseconds}ms. " +
                        $"Initial nodes: {initialNodes}. After chapters: {nodesAfterChapters}. " +
                        $"After one chapter: {nodesAfterOneChapter}. Materialized pages: {materializedPages} / 5000.");
    }

    private static int CountMaterializedNodes(IEnumerable<ProjectExplorerNode> nodes)
    {
        int count = 0;
        foreach (ProjectExplorerNode node in nodes)
        {
            if (node.Kind != ExplorerNodeKind.Placeholder)
            {
                count++;
            }

            count += CountMaterializedNodes(node.Children);
        }

        return count;
    }

    private static List<ProjectExplorerNode> GetAllNodes(IEnumerable<ProjectExplorerNode> nodes)
    {
        var list = new List<ProjectExplorerNode>();
        foreach (ProjectExplorerNode node in nodes)
        {
            if (node.Kind != ExplorerNodeKind.Placeholder)
            {
                list.Add(node);
            }

            list.AddRange(GetAllNodes(node.Children));
        }

        return list;
    }

    private sealed class StubLocalChapterSourceProvider : ILocalChapterSourceProvider
    {
        public Task<ChapterSource> LoadAsync(string sourceFolder, string projectRoot, CancellationToken cancellationToken)
            => Task.FromResult(new ChapterSource([], []));
    }
}
