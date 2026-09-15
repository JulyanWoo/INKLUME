using Inklume.Domain.Projects;

namespace Inklume.Application.Projects;

public interface IProjectStore
{
    Task<ProjectWorkspace> CreateAsync(
        TranslationProject project,
        string sourceRoot,
        CancellationToken cancellationToken);

    Task<ProjectWorkspace> OpenAsync(string sourceRoot, CancellationToken cancellationToken);
}
