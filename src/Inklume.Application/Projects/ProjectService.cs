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

    public Task<ProjectWorkspace> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRootPath(request.RootPath);

        DateTimeOffset createdAt = _timeProvider.GetUtcNow();
        var project = new TranslationProject(
            Guid.NewGuid(), request.Name, request.SeriesName, createdAt, createdAt);

        return _store.CreateAsync(project, request.RootPath, cancellationToken);
    }

    public Task<ProjectWorkspace> OpenAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRootPath(rootPath);

        return _store.OpenAsync(rootPath, cancellationToken);
    }

    private static void ValidateRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidPath, "Select a project folder before continuing.");
        }
    }
}
