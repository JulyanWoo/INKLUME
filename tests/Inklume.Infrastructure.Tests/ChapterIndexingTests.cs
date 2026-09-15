using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class ChapterIndexingTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly IWorkspaceRegistry _registry;
    private readonly FileSystemProjectStore _projectStore;
    private readonly FileSystemChapterStore _chapterStore;
    private readonly ChapterService _chapterService;

    public ChapterIndexingTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _registry = new JsonWorkspaceRegistry(_dataLocation);
        _projectStore = new FileSystemProjectStore(_dataLocation, _registry);
        _chapterStore = new FileSystemChapterStore();
        _chapterService = new ChapterService(_chapterStore, TimeProvider.System);
    }

    [Fact]
    public async Task LayoutA_DirectChapterFolder_ShouldIndexInNaturalNumericOrder()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("SeriesLayoutA");
        Directory.CreateDirectory(sourceRoot);

        // Layout A: Series/001/
        string chDir = Path.Combine(sourceRoot, "001");
        Directory.CreateDirectory(chDir);

        // Files: 1.png, 2.png, 10.png
        await File.WriteAllBytesAsync(Path.Combine(chDir, "1.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(chDir, "10.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(chDir, "2.png"), SamplePngBytes, TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        ChapterIndexResult result = await _chapterService.IndexChapterInPlaceAsync(
            workspace,
            chDir,
            new ChapterNumber(1),
            "Chapter 1",
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Pages.Count);
        // Verify Natural sort order: 1.png -> 2.png -> 10.png (NOT 1, 10, 2)
        Assert.Equal("1.png", Path.GetFileName(result.Pages[0].FilePath));
        Assert.Equal(1, result.Pages[0].Page.Number);
        Assert.Equal("2.png", Path.GetFileName(result.Pages[1].FilePath));
        Assert.Equal(2, result.Pages[1].Page.Number);
        Assert.Equal("10.png", Path.GetFileName(result.Pages[2].FilePath));
        Assert.Equal(3, result.Pages[2].Page.Number);

        // Relative paths relative to SourceRoot
        Assert.Equal(Path.Combine("001", "1.png"), result.Pages[0].Page.RelativePath);
        Assert.Equal(Path.Combine("001", "2.png"), result.Pages[1].Page.RelativePath);
        Assert.Equal(Path.Combine("001", "10.png"), result.Pages[2].Page.RelativePath);
    }

    [Fact]
    public async Task LayoutB_NestedChaptersFolder_ShouldIndexCorrectlyWithNestedRelativePaths()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("SeriesLayoutB");
        Directory.CreateDirectory(sourceRoot);

        // Layout B: Series/chapters/001/
        string chDir = Path.Combine(sourceRoot, "chapters", "001");
        Directory.CreateDirectory(chDir);

        await File.WriteAllBytesAsync(Path.Combine(chDir, "page1.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(chDir, "page2.png"), SamplePngBytes, TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        ChapterIndexResult result = await _chapterService.IndexChapterInPlaceAsync(
            workspace,
            chDir,
            new ChapterNumber(1),
            "Chapter 1",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(Path.Combine("chapters", "001", "page1.png"), result.Pages[0].Page.RelativePath);
        Assert.Equal(Path.Combine("chapters", "001", "page2.png"), result.Pages[1].Page.RelativePath);
    }

    [Fact]
    public async Task NonChapterFolder_ShouldThrowIfFolderHasNoSupportedImages()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("SeriesWithReferences");
        Directory.CreateDirectory(sourceRoot);

        string refDir = Path.Combine(sourceRoot, "references");
        Directory.CreateDirectory(refDir);
        await File.WriteAllTextAsync(Path.Combine(refDir, "notes.txt"), "No images here", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ProjectOperationException>(() =>
            _chapterService.IndexChapterInPlaceAsync(
                workspace,
                refDir,
                new ChapterNumber(99),
                "References",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LazyIndexing_OpeningFolderDoesNotEagerlyIndexChapters()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("LazySeriesFolder");
        Directory.CreateDirectory(sourceRoot);

        for (int i = 1; i <= 5; i++)
        {
            string chDir = Path.Combine(sourceRoot, $"{i:D3}");
            Directory.CreateDirectory(chDir);
            await File.WriteAllBytesAsync(Path.Combine(chDir, "01.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        }

        // Open workspace
        ProjectWorkspace workspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // Before lazy indexing: 0 chapters in DB
        IReadOnlyList<Chapter> chapters = await _chapterService.GetChaptersAsync(workspace, TestContext.Current.CancellationToken);
        Assert.Empty(chapters);

        // Lazily index chapter 1 only
        string ch1Dir = Path.Combine(sourceRoot, "001");
        await _chapterService.IndexChapterInPlaceAsync(
            workspace,
            ch1Dir,
            new ChapterNumber(1),
            "Chapter 1",
            TestContext.Current.CancellationToken);

        // Now exactly 1 chapter in DB
        chapters = await _chapterService.GetChaptersAsync(workspace, TestContext.Current.CancellationToken);
        Assert.Single(chapters);
        Assert.Equal(new ChapterNumber(1), chapters[0].Number);
    }

    public void Dispose() => _temporaryWorkspace.Dispose();
}
