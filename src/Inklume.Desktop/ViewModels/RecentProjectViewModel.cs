using Inklume.Application.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed record RecentProjectViewModel(
    Guid ProjectId,
    string Name,
    string SeriesName,
    string SourceRoot,
    DateTimeOffset LastOpenedAt)
{
    public Guid Id => ProjectId;

    public static RecentProjectViewModel FromWorkspace(ProjectWorkspace workspace)
        => FromWorkspace(workspace, workspace.Project.UpdatedAt);

    public static RecentProjectViewModel FromWorkspace(ProjectWorkspace workspace, DateTimeOffset lastOpenedAt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new RecentProjectViewModel(
            workspace.Project.Id,
            workspace.Project.Name,
            workspace.Project.SeriesName,
            workspace.SourceRoot,
            lastOpenedAt);
    }
}
