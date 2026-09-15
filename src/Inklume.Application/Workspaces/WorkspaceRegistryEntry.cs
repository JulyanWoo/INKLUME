namespace Inklume.Application.Workspaces;

public sealed record WorkspaceRegistryEntry(
    Guid ProjectId,
    string ProjectName,
    string SeriesName,
    string SourceRoot,
    string DataRoot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastOpenedAt,
    bool IsLegacyMigrated);
