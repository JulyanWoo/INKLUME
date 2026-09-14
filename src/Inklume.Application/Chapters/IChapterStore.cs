using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Application.Chapters;

public interface IChapterStore
{
    Task<ChapterImportResult> ImportAsync(
        ProjectWorkspace workspace,
        Chapter chapter,
        ChapterSource source,
        IProgress<ChapterImportProgress>? progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
        ProjectWorkspace workspace,
        Guid chapterId,
        CancellationToken cancellationToken);

    Task<PageWorkspace> EnsurePageDimensionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken);
}
