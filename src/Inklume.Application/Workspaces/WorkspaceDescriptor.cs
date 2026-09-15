namespace Inklume.Application.Workspaces;

public sealed record WorkspaceDescriptor(
    int FormatVersion,
    Guid ProjectId,
    string ProjectName,
    string SeriesName,
    string SourceRoot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastOpenedAt,
    bool IsMigratedFromLegacy);
