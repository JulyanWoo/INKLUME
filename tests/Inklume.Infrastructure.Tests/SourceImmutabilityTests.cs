using System.Security.Cryptography;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class SourceImmutabilityTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;

    public SourceImmutabilityTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _store = new FileSystemProjectStore(_dataLocation);
    }

    [Fact]
    public async Task OpenAsync_ShouldLeaveSourceFolderCompletelyUntouched()
    {
        // Setup raw comic folder structure
        string sourceRoot = _temporaryWorkspace.GetPath("UserComicSeries");
        Directory.CreateDirectory(sourceRoot);

        string ch1Dir = Path.Combine(sourceRoot, "001");
        Directory.CreateDirectory(ch1Dir);
        await File.WriteAllBytesAsync(Path.Combine(ch1Dir, "page_01.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(ch1Dir, "page_02.png"), SamplePngBytes, TestContext.Current.CancellationToken);

        string ch2Dir = Path.Combine(sourceRoot, "002");
        Directory.CreateDirectory(ch2Dir);
        await File.WriteAllBytesAsync(Path.Combine(ch2Dir, "page_01.png"), SamplePngBytes, TestContext.Current.CancellationToken);

        string notesFile = Path.Combine(sourceRoot, "readme.txt");
        await File.WriteAllTextAsync(notesFile, "User's original notes", TestContext.Current.CancellationToken);

        // Snapshot filesystem structure and content hashes BEFORE opening
        DirectorySnapshot before = DirectorySnapshot.Capture(sourceRoot);

        // Open the folder-first workspace
        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // Snapshot filesystem structure and content hashes AFTER opening
        DirectorySnapshot after = DirectorySnapshot.Capture(sourceRoot);

        // Assert that structure and content are identical
        Assert.True(before.StructureEquals(after), "Filesystem directory structure was modified during OpenAsync!");
        Assert.True(before.ContentsEqual(after), "File contents were modified during OpenAsync!");

        // Explicitly assert that INKLUME internal files were NOT created in sourceRoot
        Assert.False(File.Exists(Path.Combine(sourceRoot, "project.db")), "project.db was created in user source root!");
        Assert.False(File.Exists(Path.Combine(sourceRoot, "workspace.db")), "workspace.db was created in user source root!");
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "context")), "context/ directory was created in user source root!");
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "cache")), "cache/ directory was created in user source root!");

        // Verify that workspace data was created externally in DataRoot
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.True(Directory.Exists(workspace.ContextRoot));
        Assert.True(Directory.Exists(workspace.CacheRoot));
    }

    [Fact]
    public async Task IndexChapterInPlaceAsync_ShouldLeaveSourceFolderAndImagesByteForByteIdentical()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("MangaToScan");
        Directory.CreateDirectory(sourceRoot);

        string ch1Dir = Path.Combine(sourceRoot, "Chapter 1");
        Directory.CreateDirectory(ch1Dir);
        string page1Path = Path.Combine(ch1Dir, "01.png");
        string page2Path = Path.Combine(ch1Dir, "02.png");
        await File.WriteAllBytesAsync(page1Path, SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(page2Path, SamplePngBytes, TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // Snapshot before in-place indexing
        DirectorySnapshot before = DirectorySnapshot.Capture(sourceRoot);

        var chapterService = new ChapterService(new FileSystemChapterStore(), TimeProvider.System);
        ChapterIndexResult indexResult = await chapterService.IndexChapterInPlaceAsync(
            workspace,
            ch1Dir,
            new ChapterNumber(1),
            "Chapter 1",
            TestContext.Current.CancellationToken);

        // Snapshot after in-place indexing
        DirectorySnapshot after = DirectorySnapshot.Capture(sourceRoot);

        // Assert 100% immutability of the source folder
        Assert.True(before.StructureEquals(after), "Filesystem structure in SourceRoot changed after in-place indexing!");
        Assert.True(before.ContentsEqual(after), "File content in SourceRoot changed after in-place indexing!");

        // Assert indexed records point directly to the user images
        Assert.Equal(2, indexResult.Pages.Count);
        Assert.Equal(page1Path, indexResult.Pages[0].FilePath);
        Assert.Equal(page2Path, indexResult.Pages[1].FilePath);

        // Verify that database was updated in external DataRoot
        IReadOnlyList<Chapter> chapters = await chapterService.GetChaptersAsync(workspace, TestContext.Current.CancellationToken);
        Assert.Single(chapters);
        Assert.Equal(new ChapterNumber(1), chapters[0].Number);
    }

    [Fact]
    public void DirectorySnapshot_ComparesStructureAndContent_IgnoringVolatileOsMetadata()
    {
        string testDir = _temporaryWorkspace.GetPath("VolatileMetadataTest");
        Directory.CreateDirectory(testDir);
        string filePath = Path.Combine(testDir, "test.png");
        File.WriteAllBytes(filePath, SamplePngBytes);

        DirectorySnapshot snap1 = DirectorySnapshot.Capture(testDir);

        // Artificially modify OS volatile timestamps
        File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow.AddHours(-10));
        File.SetCreationTimeUtc(filePath, DateTime.UtcNow.AddDays(-5));

        DirectorySnapshot snap2 = DirectorySnapshot.Capture(testDir);

        // Snapshots must be considered equal because structure and content hashes match
        Assert.True(snap1.StructureEquals(snap2));
        Assert.True(snap1.ContentsEqual(snap2));
    }

    public void Dispose() => _temporaryWorkspace.Dispose();

    private sealed class DirectorySnapshot
    {
        public HashSet<string> Directories { get; }
        public Dictionary<string, string> FileHashes { get; }

        private DirectorySnapshot(HashSet<string> directories, Dictionary<string, string> fileHashes)
        {
            Directories = directories;
            FileHashes = fileHashes;
        }

        public static DirectorySnapshot Capture(string rootPath)
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(rootPath))
            {
                return new DirectorySnapshot(directories, fileHashes);
            }

            foreach (string dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                directories.Add(Path.GetRelativePath(rootPath, dir));
            }

            foreach (string file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(rootPath, file);
                byte[] bytes = File.ReadAllBytes(file);
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                fileHashes[rel] = hash;
            }

            return new DirectorySnapshot(directories, fileHashes);
        }

        public bool StructureEquals(DirectorySnapshot other)
        {
            return Directories.SetEquals(other.Directories) &&
                   FileHashes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       .SetEquals(other.FileHashes.Keys);
        }

        public bool ContentsEqual(DirectorySnapshot other)
        {
            if (!StructureEquals(other)) return false;
            foreach (var kvp in FileHashes)
            {
                if (!other.FileHashes.TryGetValue(kvp.Key, out string? otherHash) ||
                    !string.Equals(kvp.Value, otherHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
