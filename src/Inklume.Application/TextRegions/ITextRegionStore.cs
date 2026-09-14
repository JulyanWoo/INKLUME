using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public interface ITextRegionStore
{
    Task<Page?> GetPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TextRegion>> GetForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken);

    Task<int> GetNextReadingOrderAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken);

    Task AddAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken);

    Task UpdateAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken);

    Task DeleteAsync(ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken);
}
