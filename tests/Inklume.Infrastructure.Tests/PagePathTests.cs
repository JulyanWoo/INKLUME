using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class PagePathTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR42mNk+M9QzwAEjDAGBiYACc8E/a9i7sEAAAAASUVORK5CYII=");

    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly IWorkspaceRegistry _registry;
    private readonly FileSystemProjectStore _projectStore;
    private readonly FileSystemChapterStore _chapterStore;
    private readonly ChapterService _chapterService;

    public PagePathTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _registry = new JsonWorkspaceRegistry(_dataLocation);
        _projectStore = new FileSystemProjectStore(_dataLocation, _registry);
        _chapterStore = new FileSystemChapterStore();
        _chapterService = new ChapterService(_chapterStore, TimeProvider.System);
    }

    [Fact]
    public async Task PageRelativePaths_ShouldBeRelativeToSourceRoot_AndPreservedAcrossReopens()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("SeriesPagePaths");
        Directory.CreateDirectory(sourceRoot);

        string chDir = Path.Combine(sourceRoot, "001");
        Directory.CreateDirectory(chDir);
        string file1 = Path.Combine(chDir, "01.png");
        string file2 = Path.Combine(chDir, "02.png");
        await File.WriteAllBytesAsync(file1, SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(file2, SamplePngBytes, TestContext.Current.CancellationToken);

        // 1. First open: index in-place
        ProjectWorkspace workspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);
        ChapterIndexResult indexResult = await _chapterService.IndexChapterInPlaceAsync(
            workspace,
            chDir,
            new ChapterNumber(1),
            "Chapter 1",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, indexResult.Pages.Count);
        string expectedRel1 = Path.Combine("001", "01.png");
        string expectedRel2 = Path.Combine("001", "02.png");
        Assert.Equal(expectedRel1, indexResult.Pages[0].Page.RelativePath);
        Assert.Equal(expectedRel2, indexResult.Pages[1].Page.RelativePath);

        string hash1 = indexResult.Pages[0].Page.ContentHash;
        int? width1 = indexResult.Pages[0].Page.PixelWidth;
        int? height1 = indexResult.Pages[0].Page.PixelHeight;

        Assert.False(string.IsNullOrWhiteSpace(hash1));

        // 2. Reopen workspace from same sourceRoot
        ProjectWorkspace reopenedWorkspace = await _projectStore.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);
        Assert.Equal(workspace.Project.Id, reopenedWorkspace.Project.Id);

        IReadOnlyList<Chapter> chapters = await _chapterService.GetChaptersAsync(reopenedWorkspace, TestContext.Current.CancellationToken);
        Chapter chapter = Assert.Single(chapters);

        IReadOnlyList<PageWorkspace> reopenedPages = await _chapterService.GetPagesAsync(
            reopenedWorkspace, chapter.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, reopenedPages.Count);

        // Verify hashes, dimensions, relative paths, and full resolved paths match exactly
        Assert.Equal(expectedRel1, reopenedPages[0].Page.RelativePath);
        Assert.Equal(Path.GetFullPath(file1), Path.GetFullPath(reopenedPages[0].FilePath));
        Assert.Equal(hash1, reopenedPages[0].Page.ContentHash);
        Assert.Equal(width1, reopenedPages[0].Page.PixelWidth);
        Assert.Equal(height1, reopenedPages[0].Page.PixelHeight);

        Assert.Equal(expectedRel2, reopenedPages[1].Page.RelativePath);
        Assert.Equal(Path.GetFullPath(file2), Path.GetFullPath(reopenedPages[1].FilePath));
    }

    public void Dispose() => _temporaryWorkspace.Dispose();
}
