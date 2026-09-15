using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class ChapterServiceTests
{
    [Fact]
    public async Task ImportAsync_ShouldCreateChapterUsingWorkspaceAndClock()
    {
        var store = new RecordingChapterStore();
        DateTimeOffset timestamp = new(2026, 9, 13, 20, 0, 0, TimeSpan.Zero);
        var service = new ChapterService(store, new FixedTimeProvider(timestamp));
        ProjectWorkspace workspace = CreateWorkspace();
        var request = new ImportChapterRequest(
            new ChapterNumber(10.5m), " Side story ", CreateSource());

        await service.ImportAsync(workspace, request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(store.ImportedChapter);
        Assert.Equal(workspace.Project.Id, store.ImportedChapter.ProjectId);
        Assert.Equal(new ChapterNumber(10.5m), store.ImportedChapter.Number);
        Assert.Equal("Side story", store.ImportedChapter.Title);
        Assert.Equal(timestamp, store.ImportedChapter.CreatedAt);
    }

    [Fact]
    public async Task ImportAsync_ShouldRejectEmptySourceBeforeCallingStore()
    {
        var store = new RecordingChapterStore();
        var service = new ChapterService(store, TimeProvider.System);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.ImportAsync(CreateWorkspace(),
                new ImportChapterRequest(new ChapterNumber(1), null, new ChapterSource([], [])),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.NoSupportedImages, exception.Code);
        Assert.Null(store.ImportedChapter);
    }

    private static ProjectWorkspace CreateWorkspace()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        return new ProjectWorkspace(
            new TranslationProject(Guid.NewGuid(), "Project", "Series", timestamp, timestamp),
            @"C:\Project",
            @"C:\Data\Project");
    }

    private static ChapterSource CreateSource()
        => new(
            [new ChapterSourceImage("page.png", ".png", _ => ValueTask.FromResult<Stream>(new MemoryStream([1])))],
            []);

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }

    private sealed class RecordingChapterStore : IChapterStore
    {
        public Chapter? ImportedChapter { get; private set; }

        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            ImportedChapter = chapter;
            return Task.FromResult(new ChapterImportResult(workspace, chapter, [], []));
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>([]);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>([]);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterDirectory,
            ChapterNumber chapterNumber,
            string? title = null,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, now, now);
            ImportedChapter = chapter;
            return Task.FromResult(new ChapterIndexResult(workspace, chapter, []));
        }
    }
}
