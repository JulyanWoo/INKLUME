using Inklume.Domain.Projects;

namespace Inklume.Application.Projects;

public sealed class ProjectService
{
    private readonly IProjectStore _store;
    private readonly TimeProvider _timeProvider;

    public ProjectService(IProjectStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    public Task<ProjectWorkspace> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSourceRoot(request.SourceRoot);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var project = new TranslationProject(
            Guid.NewGuid(),
            request.Name,
            request.SeriesName,
            now,
            now);

        return _store.CreateAsync(project, request.SourceRoot, cancellationToken);
    }

    public Task<ProjectWorkspace> OpenAsync(string sourceRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSourceRoot(sourceRoot);

        return _store.OpenAsync(sourceRoot, cancellationToken);
    }

    private static void ValidateSourceRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The source folder path cannot be empty.");
        }
    }
}
