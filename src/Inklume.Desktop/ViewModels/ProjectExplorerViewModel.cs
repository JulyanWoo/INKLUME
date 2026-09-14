using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class ProjectExplorerViewModel : ObservableObject
{
    private readonly ProjectWorkspace _workspace;
    private readonly ChapterService _chapterService;
    private ProjectExplorerNode? _selectedNode;
    private bool _hasChapters;

    public bool HasChapters
    {
        get => _hasChapters;
        set => SetProperty(ref _hasChapters, value);
    }

    public ProjectExplorerViewModel(ProjectWorkspace workspace, ChapterService chapterService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapterService);
        _workspace = workspace;
        _chapterService = chapterService;
    }

    public ObservableCollection<ProjectExplorerNode> Roots { get; } = [];

    public ProjectExplorerNode? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            if (_selectedNode is not null)
            {
                _selectedNode.IsSelected = false;
            }

            _selectedNode = value;

            if (_selectedNode is not null)
            {
                _selectedNode.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Roots.Clear();
        SelectedNode = null;

        IReadOnlyList<Chapter> chapters = await Task.Run(
            () => _chapterService.GetChaptersAsync(_workspace, cancellationToken), cancellationToken);
        HasChapters = chapters.Count > 0;

        ProjectExplorerNode projectNode = ProjectExplorerNode.CreateProject(_workspace);
        IReadOnlyList<ProjectExplorerNode> rootEntries = DiscoverRootEntries();
        foreach (ProjectExplorerNode entry in rootEntries)
        {
            projectNode.Children.Add(entry);
        }

        Roots.Add(projectNode);
    }

    public void SelectNode(ProjectExplorerNode? node)
    {
        if (ReferenceEquals(_selectedNode, node))
        {
            return;
        }

        SelectedNode = node;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
    }

    private IReadOnlyList<ProjectExplorerNode> DiscoverRootEntries()
    {
        var entries = new List<ProjectExplorerNode>();
        string rootPath = _workspace.RootPath;

        // 1. chapters folder - always present in an INKLUME project
        string chaptersPath = Path.Combine(rootPath, "chapters");
        entries.Add(ProjectExplorerNode.CreateChaptersFolder(chaptersPath, LoadChaptersFolderAsync));

        // 2. context folder - always present in an INKLUME project
        string contextPath = Path.Combine(rootPath, "context");
        entries.Add(ProjectExplorerNode.CreateContextFolder(contextPath, LoadContextFolderAsync));

        if (!Directory.Exists(rootPath))
        {
            return entries;
        }

        try
        {
            // 3. project.db
            string dbPath = Path.Combine(rootPath, PathSafety.DatabaseFileName);
            if (File.Exists(dbPath))
            {
                entries.Add(ProjectExplorerNode.CreateProjectDatabase(dbPath));
            }

            // 4. Safe user-owned root directories (excluding cache and internal files)
            foreach (string dirPath in Directory.EnumerateDirectories(rootPath))
            {
                string dirName = Path.GetFileName(dirPath);
                if (string.Equals(dirName, "chapters", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dirName, "context", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dirName, "cache", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith('.')
                    || !IsSafePath(dirPath))
                {
                    continue;
                }

                entries.Add(ProjectExplorerNode.CreateGenericFolder(dirName, dirPath, LoadGenericFolderAsync));
            }

            // 5. Safe user-owned root files (excluding project.db, creation lock, and internal files)
            foreach (string filePath in Directory.EnumerateFiles(rootPath))
            {
                string fileName = Path.GetFileName(filePath);
                if (string.Equals(fileName, PathSafety.DatabaseFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, PathSafety.CreationLockFileName, StringComparison.OrdinalIgnoreCase)
                    || fileName.StartsWith('.')
                    || !IsSafePath(filePath))
                {
                    continue;
                }

                entries.Add(ProjectExplorerNode.CreateGenericFile(fileName, filePath));
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Failed to discover root entries in '{0}': {1}", rootPath, exception);
        }

        return entries;
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadChaptersFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        IReadOnlyList<Chapter> chapters = await Task.Run(
            () => _chapterService.GetChaptersAsync(_workspace, CancellationToken.None));
        HasChapters = chapters.Count > 0;

        Dictionary<string, Chapter> chapterByFolder = chapters
            .ToDictionary(c => c.Number.ToFolderName(), StringComparer.OrdinalIgnoreCase);

        HashSet<string> seenChapters = new(StringComparer.OrdinalIgnoreCase);
        string chaptersPath = parent.FullPath ?? Path.Combine(_workspace.RootPath, "chapters");

        if (Directory.Exists(chaptersPath))
        {
            try
            {
                foreach (string dirPath in Directory.EnumerateDirectories(chaptersPath))
                {
                    if (!IsSafePath(dirPath))
                    {
                        continue;
                    }

                    string dirName = Path.GetFileName(dirPath);
                    if (chapterByFolder.TryGetValue(dirName, out Chapter? chapter))
                    {
                        seenChapters.Add(dirName);
                        result.Add(ProjectExplorerNode.CreateChapter(chapter, dirPath, LoadChapterAsync));
                    }
                    else
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFolder(dirName, dirPath, LoadGenericFolderAsync));
                    }
                }

                foreach (string filePath in Directory.EnumerateFiles(chaptersPath))
                {
                    if (IsSafePath(filePath))
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFile(Path.GetFileName(filePath), filePath));
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Failed to enumerate chapters directory: {0}", exception);
            }
        }

        // Also add any persisted chapters that might not have matching physical folders on disk yet
        foreach (Chapter chapter in chapters)
        {
            string folderName = chapter.Number.ToFolderName();
            if (!seenChapters.Contains(folderName))
            {
                string expectedPath = Path.Combine(chaptersPath, folderName);
                result.Add(ProjectExplorerNode.CreateChapter(chapter, expectedPath, LoadChapterAsync));
            }
        }

        // Sort: Chapters by Chapter.Number, then others by DisplayName
        return [.. result.OrderBy(node => node.Kind == ExplorerNodeKind.Chapter ? 0 : 1)
            .ThenBy(node => node.Chapter?.Number.Value ?? 0m)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadChapterAsync(ProjectExplorerNode parent)
    {
        return await Task.Run(() =>
        {
            var result = new List<ProjectExplorerNode>();
            Chapter chapter = parent.Chapter
                ?? throw new InvalidOperationException("The chapter node has no chapter metadata.");

            string chapterPath = parent.FullPath
                ?? Path.Combine(_workspace.RootPath, "chapters", chapter.Number.ToFolderName());
            string expectedRawFolderName = $"{chapter.Number.ToFolderName()}_raw";

            if (!Directory.Exists(chapterPath))
            {
                string expectedRawPath = Path.Combine(chapterPath, expectedRawFolderName);
                result.Add(ProjectExplorerNode.CreateRawFolder(expectedRawFolderName, chapter, expectedRawPath, LoadRawFolderAsync));
                return result;
            }

            bool foundRawFolder = false;
            try
            {
                foreach (string dirPath in Directory.EnumerateDirectories(chapterPath))
                {
                    if (!IsSafePath(dirPath))
                    {
                        continue;
                    }

                    string dirName = Path.GetFileName(dirPath);
                    if (string.Equals(dirName, expectedRawFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        foundRawFolder = true;
                        result.Add(ProjectExplorerNode.CreateRawFolder(dirName, chapter, dirPath, LoadRawFolderAsync));
                    }
                    else
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFolder(dirName, dirPath, LoadGenericFolderAsync));
                    }
                }

                foreach (string filePath in Directory.EnumerateFiles(chapterPath))
                {
                    if (IsSafePath(filePath))
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFile(Path.GetFileName(filePath), filePath));
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Failed to enumerate chapter directory '{0}': {1}", chapterPath, exception);
            }

            if (!foundRawFolder)
            {
                string expectedRawPath = Path.Combine(chapterPath, expectedRawFolderName);
                result.Insert(0, ProjectExplorerNode.CreateRawFolder(expectedRawFolderName, chapter, expectedRawPath, LoadRawFolderAsync));
            }

            return result;
        });
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadRawFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        Chapter chapter = parent.Chapter
            ?? throw new InvalidOperationException("The raw folder node has no chapter metadata.");

        IReadOnlyList<PageWorkspace> pages = await Task.Run(
            () => _chapterService.GetPagesAsync(_workspace, chapter.Id, CancellationToken.None));

        Dictionary<string, PageWorkspace> pageByFileName = pages
            .ToDictionary(p => Path.GetFileName(p.Page.RelativePath), StringComparer.OrdinalIgnoreCase);

        HashSet<string> seenPages = new(StringComparer.OrdinalIgnoreCase);
        string rawPath = parent.FullPath ?? string.Empty;

        if (Directory.Exists(rawPath))
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(rawPath))
                {
                    if (!IsSafePath(filePath))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(filePath);
                    if (pageByFileName.TryGetValue(fileName, out PageWorkspace? pageWorkspace))
                    {
                        seenPages.Add(fileName);
                        result.Add(ProjectExplorerNode.CreatePage(chapter, pageWorkspace));
                    }
                    else
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFile(fileName, filePath));
                    }
                }

                foreach (string dirPath in Directory.EnumerateDirectories(rawPath))
                {
                    if (IsSafePath(dirPath))
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFolder(Path.GetFileName(dirPath), dirPath, LoadGenericFolderAsync));
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Failed to enumerate raw folder '{0}': {1}", rawPath, exception);
            }
        }

        // Also add any persisted pages that weren't found on disk directly
        foreach (PageWorkspace page in pages)
        {
            string fileName = Path.GetFileName(page.Page.RelativePath);
            if (!seenPages.Contains(fileName))
            {
                result.Add(ProjectExplorerNode.CreatePage(chapter, page));
            }
        }

        // Sort: Pages by Page.Number, then others by DisplayName
        return [.. result.OrderBy(node => node.Kind == ExplorerNodeKind.Page ? 0 : 1)
            .ThenBy(node => node.Page?.Page.Number ?? 0)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    private Task<IReadOnlyList<ProjectExplorerNode>> LoadContextFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        string contextPath = parent.FullPath ?? Path.Combine(_workspace.RootPath, "context");

        if (!Directory.Exists(contextPath))
        {
            return Task.FromResult<IReadOnlyList<ProjectExplorerNode>>(result);
        }

        string[] reservedFiles = ["series.json", "characters.json", "glossary.json", "translation_rules.json"];

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(contextPath))
            {
                if (!IsSafePath(filePath))
                {
                    continue;
                }

                string fileName = Path.GetFileName(filePath);
                if (reservedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(ProjectExplorerNode.CreateContextFile(fileName, filePath));
                }
                else
                {
                    result.Add(ProjectExplorerNode.CreateGenericFile(fileName, filePath));
                }
            }

            foreach (string dirPath in Directory.EnumerateDirectories(contextPath))
            {
                if (IsSafePath(dirPath))
                {
                    result.Add(ProjectExplorerNode.CreateGenericFolder(Path.GetFileName(dirPath), dirPath, LoadGenericFolderAsync));
                }
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Failed to enumerate context folder '{0}': {1}", contextPath, exception);
        }

        // Sort: Context files first (ordered by reserved list), then others alphabetically
        return Task.FromResult<IReadOnlyList<ProjectExplorerNode>>([.. result
            .OrderBy(node => node.Kind == ExplorerNodeKind.ContextFile ? 0 : 1)
            .ThenBy(node => node.Kind == ExplorerNodeKind.ContextFile ? Array.IndexOf(reservedFiles, node.DisplayName.ToLowerInvariant()) : 999)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)]);
    }

    private Task<IReadOnlyList<ProjectExplorerNode>> LoadGenericFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        string folderPath = parent.FullPath ?? string.Empty;

        if (!Directory.Exists(folderPath) || !IsSafePath(folderPath))
        {
            return Task.FromResult<IReadOnlyList<ProjectExplorerNode>>(result);
        }

        try
        {
            foreach (string dirPath in Directory.EnumerateDirectories(folderPath))
            {
                if (!IsSafePath(dirPath))
                {
                    continue;
                }

                string dirName = Path.GetFileName(dirPath);
                result.Add(ProjectExplorerNode.CreateGenericFolder(dirName, dirPath, LoadGenericFolderAsync));
            }

            foreach (string filePath in Directory.EnumerateFiles(folderPath))
            {
                if (!IsSafePath(filePath))
                {
                    continue;
                }

                string fileName = Path.GetFileName(filePath);
                result.Add(ProjectExplorerNode.CreateGenericFile(fileName, filePath));
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Failed to enumerate generic folder '{0}': {1}", folderPath, exception);
        }

        return Task.FromResult<IReadOnlyList<ProjectExplorerNode>>([.. result
            .OrderBy(node => node.Kind == ExplorerNodeKind.GenericFolder ? 0 : 1)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)]);
    }

    private bool IsSafePath(string candidatePath)
    {
        try
        {
            if (!PathSafety.IsDescendantOf(_workspace.RootPath, candidatePath))
            {
                return false;
            }

            if (PathSafety.IsReparsePoint(candidatePath))
            {
                string target = Path.GetFullPath(candidatePath);
                if (!PathSafety.IsDescendantOf(_workspace.RootPath, target))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
