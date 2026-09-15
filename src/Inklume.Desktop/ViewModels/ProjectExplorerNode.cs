using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class ProjectExplorerNode : ObservableObject
{
    private readonly Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? _childrenLoader;
    private Task? _loadingTask;

    private ProjectExplorerNode(
        ExplorerNodeKind kind,
        string displayName,
        string? secondaryText = null,
        string? fullPath = null,
        Chapter? chapter = null,
        PageWorkspace? page = null,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null,
        bool isChapterCandidate = false)
    {
        Kind = kind;
        DisplayName = displayName;
        SecondaryText = secondaryText;
        FullPath = fullPath;
        Chapter = chapter;
        Page = page;
        _childrenLoader = childrenLoader;
        IsChapterCandidate = isChapterCandidate;

        if (_childrenLoader is not null)
        {
            Children.Add(CreatePlaceholder());
        }
        else
        {
            IsLoaded = true;
        }
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value && !IsLoaded && _childrenLoader is not null)
            {
                _ = EnsureLoadedAsync();
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsLoaded { get; private set; }

    public bool IsChapterCandidate { get; }

    public bool HasLooseImages { get; set; }

    public ExplorerNodeKind Kind { get; }

    public string DisplayName { get; }

    public string? SecondaryText { get; }

    public string? FullPath { get; }

    public Chapter? Chapter { get; }

    public PageWorkspace? Page { get; }

    public ObservableCollection<ProjectExplorerNode> Children { get; } = [];

    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded || _childrenLoader is null)
        {
            return;
        }

        if (_loadingTask is not null)
        {
            await _loadingTask;
            return;
        }

        _loadingTask = LoadChildrenCoreAsync();
        try
        {
            await _loadingTask;
        }
        finally
        {
            _loadingTask = null;
        }
    }

    public async Task ReloadAsync()
    {
        if (_childrenLoader is null)
        {
            return;
        }

        IsLoaded = false;
        await EnsureLoadedAsync();
    }

    private async Task LoadChildrenCoreAsync()
    {
        if (IsLoaded || _childrenLoader is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<ProjectExplorerNode> loadedChildren = await _childrenLoader(this);
            Children.Clear();
            foreach (ProjectExplorerNode child in loadedChildren)
            {
                Children.Add(child);
            }

            IsLoaded = true;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Failed to load children for explorer node '{0}': {1}", DisplayName, exception);
            Children.Clear();
            IsLoaded = true;
        }
    }

    public static ProjectExplorerNode CreateProject(
        ProjectWorkspace workspace,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.Project,
            workspace.Project.Name,
            null,
            workspace.SourceRoot,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreateChaptersFolder(
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.ChaptersFolder,
            "chapters",
            null,
            fullPath,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreateChapter(
        string folderName,
        Chapter? chapter,
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.Chapter,
            chapter is null ? folderName : $"Chapter {chapter.Number}",
            chapter?.Title,
            fullPath,
            chapter: chapter,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreateChapter(
        Chapter chapter,
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.Chapter,
            $"Chapter {chapter.Number}",
            chapter.Title,
            fullPath,
            chapter: chapter,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreateRawFolder(
        string folderName,
        Chapter chapter,
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.RawFolder,
            folderName,
            null,
            fullPath,
            chapter: chapter,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreatePage(Chapter? chapter, PageWorkspace page)
        => new(
            ExplorerNodeKind.Page,
            Path.GetFileName(page.Page.RelativePath),
            page.Page.OriginalFileName,
            page.FilePath,
            chapter: chapter,
            page: page);

    public static ProjectExplorerNode CreateContextFolder(
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null)
        => new(
            ExplorerNodeKind.ContextFolder,
            "context",
            null,
            fullPath,
            childrenLoader: childrenLoader);

    public static ProjectExplorerNode CreateContextFile(string fileName, string fullPath)
        => new(
            ExplorerNodeKind.ContextFile,
            fileName,
            null,
            fullPath);

    public static ProjectExplorerNode CreateProjectDatabase(string fullPath)
        => CreateProjectDatabase(Path.GetFileName(fullPath), fullPath);

    public static ProjectExplorerNode CreateProjectDatabase(string fileName, string fullPath)
        => new(
            ExplorerNodeKind.ProjectDatabase,
            fileName,
            null,
            fullPath);

    public static ProjectExplorerNode CreateGenericFolder(
        string folderName,
        string fullPath,
        Func<ProjectExplorerNode, Task<IReadOnlyList<ProjectExplorerNode>>>? childrenLoader = null,
        bool isChapterCandidate = false)
        => new(
            ExplorerNodeKind.GenericFolder,
            folderName,
            null,
            fullPath,
            childrenLoader: childrenLoader,
            isChapterCandidate: isChapterCandidate);

    public static ProjectExplorerNode CreateGenericFile(string fileName, string fullPath)
        => new(
            ExplorerNodeKind.GenericFile,
            fileName,
            null,
            fullPath);

    public static ProjectExplorerNode CreateGenericImageFile(string fileName, string fullPath)
        => new(
            ExplorerNodeKind.GenericImageFile,
            fileName,
            null,
            fullPath);

    public static ProjectExplorerNode CreatePlaceholder()
        => new(
            ExplorerNodeKind.Placeholder,
            "Loading...",
            null,
            null);
}
