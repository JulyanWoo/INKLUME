using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Inklume.Application.Chapters;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class ProjectExplorerNode : ObservableObject
{
    private ProjectExplorerNode(
        ExplorerNodeKind kind,
        string displayName,
        string? secondaryText = null,
        Chapter? chapter = null,
        PageWorkspace? page = null)
    {
        Kind = kind;
        DisplayName = displayName;
        SecondaryText = secondaryText;
        Chapter = chapter;
        Page = page;
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ExplorerNodeKind Kind { get; }

    public string DisplayName { get; }

    public string? SecondaryText { get; }

    public Chapter? Chapter { get; }

    public PageWorkspace? Page { get; }

    public ObservableCollection<ProjectExplorerNode> Children { get; } = [];

    public static ProjectExplorerNode CreateProject(ProjectWorkspace workspace)
        => new(ExplorerNodeKind.Project, workspace.Project.Name, workspace.Project.SeriesName)
        {
            IsExpanded = true
        };

    public static ProjectExplorerNode CreateChaptersGroup()
        => new(ExplorerNodeKind.Chapters, "Chapters")
        {
            IsExpanded = true
        };

    public static ProjectExplorerNode CreateChapter(Chapter chapter)
        => new(ExplorerNodeKind.Chapter, $"Chapter {chapter.Number}", chapter.Title, chapter);

    public static ProjectExplorerNode CreatePage(Chapter chapter, PageWorkspace page)
        => new(
            ExplorerNodeKind.Page,
            Path.GetFileName(page.Page.RelativePath),
            page.Page.OriginalFileName,
            chapter,
            page);
}
