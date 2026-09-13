using Inklume.Application.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed record RecentProjectViewModel(
    Guid ProjectId,
    string Name,
    string SeriesName,
    string RootPath,
    DateTimeOffset LastOpenedAt)
{
    public static RecentProjectViewModel FromWorkspace(ProjectWorkspace workspace, DateTimeOffset lastOpenedAt)
        => new(
            workspace.Project.Id,
            workspace.Project.Name,
            workspace.Project.SeriesName,
            workspace.RootPath,
            lastOpenedAt);
}
