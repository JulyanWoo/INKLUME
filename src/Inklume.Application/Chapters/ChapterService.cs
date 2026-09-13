using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Application.Chapters;

public sealed class ChapterService(IChapterStore store, TimeProvider timeProvider)
{
    public Task<ChapterImportResult> ImportAsync(
        ProjectWorkspace workspace,
        ImportChapterRequest request,
        IProgress<ChapterImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Source.Images.Count == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.NoSupportedImages,
                "The chapter source contains no supported images.");
        }

        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, request.Number, request.Title, timestamp, timestamp);
        return store.ImportAsync(workspace, chapter, request.Source, progress, cancellationToken);
    }

    public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        return store.GetChaptersAsync(workspace, cancellationToken);
    }

    public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
        ProjectWorkspace workspace,
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (chapterId == Guid.Empty)
        {
            throw new ArgumentException("The chapter identifier must not be empty.", nameof(chapterId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return store.GetPagesAsync(workspace, chapterId, cancellationToken);
    }
}
