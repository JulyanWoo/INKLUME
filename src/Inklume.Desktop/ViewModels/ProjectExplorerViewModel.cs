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
        IReadOnlyList<ProjectExplorerNode> rootEntries = await Task.Run(
            () => DiscoverDirectoryEntries(_workspace.SourceRoot, chapters), cancellationToken);

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

    public async Task RefreshChaptersAsync()
    {
        ProjectExplorerNode? chaptersNode = Roots.FirstOrDefault()?.Children
            .FirstOrDefault(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
        if (chaptersNode is not null)
        {
            await chaptersNode.ReloadAsync();
        }
        else
        {
            await InitializeAsync();
        }
    }

    private IReadOnlyList<ProjectExplorerNode> DiscoverDirectoryEntries(
        string directoryPath, IReadOnlyList<Chapter> persistedChapters)
    {
        var entries = new List<ProjectExplorerNode>();
        bool isRoot = string.Equals(
            Path.GetFullPath(directoryPath),
            Path.GetFullPath(_workspace.SourceRoot),
            StringComparison.OrdinalIgnoreCase);

        if (!Directory.Exists(directoryPath))
        {
            if (isRoot)
            {
                // In-memory or mock workspace support
                entries.Add(ProjectExplorerNode.CreateChaptersFolder(
                    Path.Combine(directoryPath, "chapters"), LoadChaptersFolderAsync));
                entries.Add(ProjectExplorerNode.CreateContextFolder(
                    Path.Combine(directoryPath, "context"), LoadContextFolderAsync));
            }

            return entries;
        }

        if (!IsSafePath(directoryPath))
        {
            return entries;
        }

        try
        {
            if (isRoot)
            {
                string chaptersDir = Path.Combine(directoryPath, "chapters");
                if (Directory.Exists(chaptersDir) || persistedChapters.Count > 0)
                {
                    entries.Add(ProjectExplorerNode.CreateChaptersFolder(chaptersDir, LoadChaptersFolderAsync));
                }

                string contextDir = Path.Combine(directoryPath, "context");
                if (Directory.Exists(contextDir))
                {
                    entries.Add(ProjectExplorerNode.CreateContextFolder(contextDir, LoadContextFolderAsync));
                }

                foreach (string dirPath in Directory.EnumerateDirectories(directoryPath))
                {
                    string dirName = Path.GetFileName(dirPath);
                    if (dirName.StartsWith('.') || !IsSafePath(dirPath))
                    {
                        continue;
                    }

                    if (dirName.Equals("chapters", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("context", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("cache", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // handled explicitly above or hidden (cache)
                    }

                    bool isCandidate = (dirName.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase) ||
                                        ChapterNumber.TryParse(dirName, out _)) &&
                                       ContainsSupportedImagesDirectly(dirPath);
                    entries.Add(ProjectExplorerNode.CreateGenericFolder(
                        dirName, dirPath, LoadGenericFolderAsync, isChapterCandidate: isCandidate));
                }

                foreach (string filePath in Directory.EnumerateFiles(directoryPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (fileName.StartsWith('.') || !IsSafePath(filePath))
                    {
                        continue;
                    }

                    if (fileName.Equals("project.db", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("workspace.db", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(ProjectExplorerNode.CreateProjectDatabase(fileName, filePath));
                    }
                    else
                    {
                        entries.Add(CreateFileNode(fileName, filePath));
                    }
                }

                return [.. entries
                    .OrderBy(node => node.Kind switch {
                        ExplorerNodeKind.ChaptersFolder => 0,
                        ExplorerNodeKind.ContextFolder => 1,
                        ExplorerNodeKind.ProjectDatabase => 2,
                        ExplorerNodeKind.GenericFolder => 3,
                        ExplorerNodeKind.GenericImageFile => 4,
                        _ => 5
                    })
                    .ThenBy(node => node.DisplayName, NaturalFileNameComparer.Instance)];
            }

            // Subfolder enumeration
            foreach (string dirPath in Directory.EnumerateDirectories(directoryPath))
            {
                string dirName = Path.GetFileName(dirPath);
                if (dirName.StartsWith('.') || dirName.Equals("cache", StringComparison.OrdinalIgnoreCase) || !IsSafePath(dirPath))
                {
                    continue;
                }

                bool isCandidate = ContainsSupportedImagesDirectly(dirPath);
                entries.Add(ProjectExplorerNode.CreateGenericFolder(
                    dirName, dirPath, LoadGenericFolderAsync, isChapterCandidate: isCandidate));
            }

            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith('.') || !IsSafePath(filePath))
                {
                    continue;
                }

                entries.Add(CreateFileNode(fileName, filePath));
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Failed to discover entries in '{0}': {1}", directoryPath, exception);
        }

        return [.. entries
            .OrderBy(node => node.Kind == ExplorerNodeKind.GenericFolder ? 0 : 1)
            .ThenBy(node => node.DisplayName, NaturalFileNameComparer.Instance)];
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadChaptersFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        IReadOnlyList<Chapter> chapters = await Task.Run(
            () => _chapterService.GetChaptersAsync(_workspace, CancellationToken.None));
        HasChapters = chapters.Count > 0;

        Dictionary<string, Chapter> chapterByFolder = chapters
            .ToDictionary(c => c.Number.ToFolderName(), StringComparer.OrdinalIgnoreCase);
        Dictionary<decimal, Chapter> chapterByNumber = chapters
            .ToDictionary(c => c.Number.Value);

        HashSet<Guid> seenChapterIds = [];
        string chaptersPath = parent.FullPath ?? Path.Combine(_workspace.SourceRoot, "chapters");

        if (Directory.Exists(chaptersPath))
        {
            try
            {
                foreach (string dirPath in Directory.EnumerateDirectories(chaptersPath))
                {
                    if (!IsSafePath(dirPath) || Path.GetFileName(dirPath).StartsWith('.'))
                    {
                        continue;
                    }

                    string dirName = Path.GetFileName(dirPath);
                    if (chapterByFolder.TryGetValue(dirName, out Chapter? chapter) ||
                        (ChapterNumber.TryParse(dirName, out ChapterNumber parsedNumber) && chapterByNumber.TryGetValue(parsedNumber.Value, out chapter)))
                    {
                        seenChapterIds.Add(chapter.Id);
                        result.Add(ProjectExplorerNode.CreateChapter(chapter, dirPath, LoadChapterAsync));
                    }
                    else
                    {
                        bool isCandidate = ContainsSupportedImagesDirectly(dirPath);
                        result.Add(ProjectExplorerNode.CreateGenericFolder(
                            dirName, dirPath, LoadGenericFolderAsync, isChapterCandidate: isCandidate));
                    }
                }

                foreach (string filePath in Directory.EnumerateFiles(chaptersPath))
                {
                    if (!IsSafePath(filePath) || Path.GetFileName(filePath).StartsWith('.'))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(filePath);
                    result.Add(CreateFileNode(fileName, filePath));
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Failed to enumerate chapters directory: {0}", exception);
            }
        }

        // Also add any persisted chapters that do not have matching physical directories on disk yet
        foreach (Chapter chapter in chapters)
        {
            if (!seenChapterIds.Contains(chapter.Id))
            {
                string expectedPath = Path.Combine(chaptersPath, chapter.Number.ToFolderName());
                result.Add(ProjectExplorerNode.CreateChapter(chapter, expectedPath, LoadChapterAsync));
            }
        }

        return [.. result
            .OrderBy(node => node.Kind == ExplorerNodeKind.Chapter ? 0 : (node.Kind == ExplorerNodeKind.GenericFolder ? 1 : 2))
            .ThenBy(node => node.Chapter?.Number.Value ?? 0m)
            .ThenBy(node => node.DisplayName, NaturalFileNameComparer.Instance)];
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadChapterAsync(ProjectExplorerNode parent)
    {
        return await Task.Run(() =>
        {
            var result = new List<ProjectExplorerNode>();
            Chapter chapter = parent.Chapter
                ?? throw new InvalidOperationException("The chapter node has no chapter metadata.");

            string chapterPath = parent.FullPath
                ?? Path.Combine(_workspace.SourceRoot, "chapters", chapter.Number.ToFolderName());
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
                    if (!IsSafePath(dirPath) || Path.GetFileName(dirPath).StartsWith('.'))
                    {
                        continue;
                    }

                    string dirName = Path.GetFileName(dirPath);
                    if (string.Equals(dirName, expectedRawFolderName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "raw", StringComparison.OrdinalIgnoreCase))
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
                    if (!IsSafePath(filePath) || Path.GetFileName(filePath).StartsWith('.'))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(filePath);
                    result.Add(CreateFileNode(fileName, filePath));
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
                    if (!IsSafePath(filePath) || Path.GetFileName(filePath).StartsWith('.'))
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
                        result.Add(CreateFileNode(fileName, filePath));
                    }
                }

                foreach (string dirPath in Directory.EnumerateDirectories(rawPath))
                {
                    if (IsSafePath(dirPath) && !Path.GetFileName(dirPath).StartsWith('.'))
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFolder(
                            Path.GetFileName(dirPath), dirPath, LoadGenericFolderAsync));
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

        return [.. result
            .OrderBy(node => node.Kind == ExplorerNodeKind.Page ? 0 : 1)
            .ThenBy(node => node.Page?.Page.Number ?? 0)
            .ThenBy(node => node.DisplayName, NaturalFileNameComparer.Instance)];
    }

    private Task<IReadOnlyList<ProjectExplorerNode>> LoadContextFolderAsync(ProjectExplorerNode parent)
    {
        var result = new List<ProjectExplorerNode>();
        string contextPath = parent.FullPath ?? Path.Combine(_workspace.SourceRoot, "context");
        string[] reservedFiles = ["series.json", "characters.json", "glossary.json", "translation_rules.json"];

        if (Directory.Exists(contextPath))
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(contextPath))
                {
                    if (!IsSafePath(filePath) || Path.GetFileName(filePath).StartsWith('.'))
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
                        result.Add(CreateFileNode(fileName, filePath));
                    }
                }

                foreach (string dirPath in Directory.EnumerateDirectories(contextPath))
                {
                    if (IsSafePath(dirPath) && !Path.GetFileName(dirPath).StartsWith('.'))
                    {
                        result.Add(ProjectExplorerNode.CreateGenericFolder(
                            Path.GetFileName(dirPath), dirPath, LoadGenericFolderAsync));
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Failed to enumerate context folder '{0}': {1}", contextPath, exception);
            }
        }

        return Task.FromResult<IReadOnlyList<ProjectExplorerNode>>([.. result
            .OrderBy(node => node.Kind == ExplorerNodeKind.ContextFile ? 0 : 1)
            .ThenBy(node => node.Kind == ExplorerNodeKind.ContextFile ? Array.IndexOf(reservedFiles, node.DisplayName.ToLowerInvariant()) : 999)
            .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)]);
    }

    private async Task<IReadOnlyList<ProjectExplorerNode>> LoadGenericFolderAsync(ProjectExplorerNode parent)
    {
        string folderPath = parent.FullPath ?? string.Empty;
        if (!Directory.Exists(folderPath) || !IsSafePath(folderPath))
        {
            return [];
        }

        IReadOnlyList<Chapter> persistedChapters = await Task.Run(
            () => _chapterService.GetChaptersAsync(_workspace, CancellationToken.None));

        return await Task.Run(() => DiscoverDirectoryEntries(folderPath, persistedChapters));
    }

    private static bool TryRecognizeChapterFolder(
        string dirPath,
        IReadOnlyDictionary<string, Chapter> chapterByFolder,
        IReadOnlyDictionary<decimal, Chapter> chapterByNumber,
        out Chapter? chapter,
        out ChapterNumber chapterNumber)
    {
        string folderName = Path.GetFileName(dirPath);

        if (chapterByFolder.TryGetValue(folderName, out chapter))
        {
            chapterNumber = chapter.Number;
            return true;
        }

        if (ChapterNumber.TryParse(folderName, out chapterNumber))
        {
            if (chapterByNumber.TryGetValue(chapterNumber.Value, out chapter))
            {
                return true;
            }

            if (ContainsSupportedImagesDirectly(dirPath))
            {
                chapter = null;
                return true;
            }
        }

        chapter = null;
        chapterNumber = default;
        return false;
    }

    private static ProjectExplorerNode CreateFileNode(string fileName, string filePath)
    {
        return SupportedImageFormats.IsSupported(filePath)
            ? ProjectExplorerNode.CreateGenericImageFile(fileName, filePath)
            : ProjectExplorerNode.CreateGenericFile(fileName, filePath);
    }

    private static bool ContainsSupportedImagesDirectly(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
            {
                return false;
            }

            foreach (string file in Directory.EnumerateFiles(dirPath))
            {
                if (SupportedImageFormats.IsSupported(file))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore access or IO exceptions during discovery
        }

        return false;
    }

    private bool IsSafePath(string candidatePath)
    {
        try
        {
            if (!PathSafety.IsDescendantOf(_workspace.SourceRoot, candidatePath))
            {
                return false;
            }

            if (PathSafety.IsReparsePoint(candidatePath))
            {
                string target = Path.GetFullPath(candidatePath);
                if (!PathSafety.IsDescendantOf(_workspace.SourceRoot, target))
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
