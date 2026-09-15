namespace Inklume.Application.Workspaces;

public interface IWorkspaceRegistry
{
    Task<WorkspaceRegistryEntry?> FindBySourceRootAsync(string sourceRoot, CancellationToken cancellationToken);

    Task<WorkspaceRegistryEntry?> FindByIdAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceRegistryEntry>> GetAllAsync(CancellationToken cancellationToken);

    Task RegisterOrUpdateAsync(WorkspaceRegistryEntry entry, CancellationToken cancellationToken);

    Task RelocateSourceAsync(Guid projectId, string newSourceRoot, CancellationToken cancellationToken);

    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken);
}
