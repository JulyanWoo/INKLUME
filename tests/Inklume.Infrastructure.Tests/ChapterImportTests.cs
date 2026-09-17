using System.Security.Cryptography;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class ChapterImportTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] WebpBytes = Convert.FromBase64String(
        "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEAAUAmJaQAA3AA/vuU");
    private static readonly byte[] MinimalJpegBytes =
    [
        0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01, 0x00, 0x01, 0x03,
        0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00, 0xFF, 0xD9
    ];
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly IProjectStore _projectStore;

    public ChapterImportTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _projectStore = new FileSystemProjectStore(_dataLocation);
    }

    [Fact]
    public async Task ImportAsync_ShouldCopySupportedImagesInNaturalOrderAndPreserveSources()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Project with spaces");
        string sourcePath = CreateSourceFolder("Unicode source 日本語");
        WriteImage(sourcePath, "página_10.png", PngBytes);
        WriteImage(sourcePath, "página_2.JPG", MinimalJpegBytes);
        WriteImage(sourcePath, "página_1.webp", WebpBytes);
        await File.WriteAllTextAsync(
            Path.Combine(sourcePath, "notas_日本語.txt"), "ignored", TestContext.Current.CancellationToken);
        FileState[] sourcesBefore = CaptureFiles(sourcePath);

        ChapterImportResult result = await ImportLocalAsync(
            workspace, new ChapterNumber(1), "Opening", sourcePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["página_1.webp", "página_2.JPG", "página_10.png"],
            result.Pages.Select(page => page.Page.OriginalFileName));
        Assert.Equal(["001.webp", "002.jpg", "003.png"],
            result.Pages.Select(page => Path.GetFileName(page.FilePath)));
        Assert.Equal([1, 2, 3], result.Pages.Select(page => page.Page.Number));
        Assert.Equal(["notas_日本語.txt"], result.IgnoredFiles);
        Assert.Equal(sourcesBefore, CaptureFiles(sourcePath));
        Assert.All(result.Pages, page => Assert.True(File.Exists(page.FilePath)));
        Assert.All(result.Pages, page =>
        {
            Assert.Equal(1, page.Page.PixelWidth);
            Assert.Equal(1, page.Page.PixelHeight);
        });
        Assert.All(result.Pages, page => Assert.Equal(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(page.FilePath))), page.Page.ContentHash));
        Assert.Equal(Path.Combine(workspace.SourceRoot, "chapters", "001", "001_raw"),
            Path.GetDirectoryName(result.Pages[0].FilePath));
    }

    [Fact]
    public async Task ImportAsync_ShouldPersistChapterPagesAndOrderAcrossReopen()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Persistence project");
        string sourcePath = CreateSourceFolder("Ordered images");
        WriteImage(sourcePath, "10.png", PngBytes);
        WriteImage(sourcePath, "2.png", PngBytes);
        WriteImage(sourcePath, "1.png", PngBytes);
        ChapterService service = CreateChapterService();
        ChapterImportResult imported = await ImportLocalAsync(
            workspace, new ChapterNumber(10.5m), null, sourcePath,
            cancellationToken: TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await _projectStore.OpenAsync(
            workspace.SourceRoot, TestContext.Current.CancellationToken);
        IReadOnlyList<Chapter> chapters = await service.GetChaptersAsync(reopened, TestContext.Current.CancellationToken);
        IReadOnlyList<PageWorkspace> pages = await service.GetPagesAsync(
            reopened, chapters.Single().Id, TestContext.Current.CancellationToken);

        Assert.Equal(imported.Chapter, Assert.Single(chapters));
        Assert.Equal(["1.png", "2.png", "10.png"], pages.Select(page => page.Page.OriginalFileName));
        Assert.Equal([1, 2, 3], pages.Select(page => page.Page.Number));
        Assert.All(pages, page => Assert.False(Path.IsPathFullyQualified(page.Page.RelativePath)));
        Assert.All(pages, page => Assert.StartsWith(reopened.SourceRoot, page.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.True(reopened.Project.UpdatedAt >= workspace.Project.UpdatedAt);
    }

    [Fact]
    public async Task ImportAsync_ShouldRejectDuplicateChapterWithoutChangingFilesOrDatabase()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Duplicate project");
        string sourcePath = CreateSourceFolder("Source");
        WriteImage(sourcePath, "1.png", PngBytes);
        ChapterService service = CreateChapterService();
        await ImportLocalAsync(workspace, new ChapterNumber(12), null, sourcePath,
            cancellationToken: TestContext.Current.CancellationToken);
        FileState[] before = CaptureFiles(workspace.SourceRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            ImportLocalAsync(workspace, new ChapterNumber(12.0m), "Duplicate", sourcePath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.DuplicateChapter, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.SourceRoot));
        Assert.Single(await service.GetChaptersAsync(workspace, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_ShouldRemoveOwnedPartialFilesAndNotPersist_WhenCancelled()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Cancellation project");
        string sourcePath = CreateSourceFolder("Large source");
        for (int index = 1; index <= 4; index++)
        {
            WriteImage(sourcePath, $"{index}.png", PngBytes);
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<ChapterImportProgress>(value =>
        {
            if (value.Completed == 1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImportLocalAsync(workspace, new ChapterNumber(2), null, sourcePath, progress, cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(workspace.SourceRoot, "chapters")));
        Assert.Empty(await CreateChapterService().GetChaptersAsync(workspace, TestContext.Current.CancellationToken));
        Assert.Equal(4, Directory.EnumerateFiles(sourcePath).Count());
    }

    [Fact]
    public async Task ImportAsync_ShouldRemovePromotedChapter_WhenSqlitePersistenceFails()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Persistence failure project");
        string sourcePath = CreateSourceFolder("Persistence failure source");
        WriteImage(sourcePath, "1.png", PngBytes);
        FileState[] sourcesBefore = CaptureFiles(sourcePath);
        string databasePath = workspace.DatabasePath;
        await ExecuteSqlAsync(databasePath,
            "CREATE TRIGGER FailChapterInsert BEFORE INSERT ON Chapters " +
            "BEGIN SELECT RAISE(ABORT, 'forced chapter persistence failure'); END;");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            ImportLocalAsync(workspace, new ChapterNumber(2.5m), null, sourcePath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.ImportFailure, exception.Code);
        Assert.False(Directory.Exists(Path.Combine(workspace.SourceRoot, "chapters", "002.5")));
        string chaptersDir = Path.Combine(workspace.SourceRoot, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(chaptersDir));
        }
        Assert.Empty(await CreateChapterService().GetChaptersAsync(workspace, TestContext.Current.CancellationToken));
        Assert.Equal(sourcesBefore, CaptureFiles(sourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_ShouldRejectFolderWithoutUsableImages(bool includeUnsupportedFile)
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Empty source project");
        string sourcePath = CreateSourceFolder("Empty source");
        if (includeUnsupportedFile)
        {
            await File.WriteAllTextAsync(Path.Combine(sourcePath, "readme.txt"), "not an image", TestContext.Current.CancellationToken);
        }

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            ImportLocalAsync(workspace, new ChapterNumber(3), null, sourcePath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.NoSupportedImages, exception.Code);
        Assert.Empty(await CreateChapterService().GetChaptersAsync(workspace, TestContext.Current.CancellationToken));
        string chaptersDir = Path.Combine(workspace.SourceRoot, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(chaptersDir));
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldRejectCorruptSupportedImageWithoutPartialChapter()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Corrupt image project");
        string sourcePath = CreateSourceFolder("Corrupt source");
        WriteImage(sourcePath, "1.png", PngBytes);
        WriteImage(sourcePath, "2.png", [0x89, 0x50, 0x4E, 0x47]);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            ImportLocalAsync(workspace, new ChapterNumber(4), null, sourcePath,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidImage, exception.Code);
        Assert.Empty(await CreateChapterService().GetChaptersAsync(workspace, TestContext.Current.CancellationToken));
        string chaptersDir = Path.Combine(workspace.SourceRoot, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(chaptersDir));
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldStoreSameHashForIdenticalContent()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Hash project");
        string sourcePath = CreateSourceFolder("Hash source");
        WriteImage(sourcePath, "copy 1.png", PngBytes);
        WriteImage(sourcePath, "copy 2.png", PngBytes);

        ChapterImportResult result = await ImportLocalAsync(
            workspace, new ChapterNumber(5), null, sourcePath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(result.Pages[0].Page.ContentHash, result.Pages[1].Page.ContentHash);
    }

    [Fact]
    public async Task OpenAsync_ShouldUpgradeProjectCreatedWithInitialMigration()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Legacy schema project");
        string databasePath = workspace.DatabasePath;
        await ExecuteSqlAsync(databasePath,
            "DROP TABLE IF EXISTS OcrRecognitions; DROP TABLE IF EXISTS TextRegionPoints; DROP TABLE IF EXISTS TextRegions; DROP TABLE IF EXISTS Pages; DROP TABLE IF EXISTS Chapters; " +
            "DELETE FROM __EFMigrationsHistory WHERE MigrationId NOT LIKE '%InitialProject';");

        ProjectWorkspace reopened = await _projectStore.OpenAsync(
            workspace.SourceRoot, TestContext.Current.CancellationToken);

        Assert.Equal(workspace.Project.Id, reopened.Project.Id);
        Assert.True(await TableExistsAsync(databasePath, "Chapters"));
        Assert.True(await TableExistsAsync(databasePath, "Pages"));
    }

    [Fact]
    public async Task Database_ShouldRejectDuplicateChapterNumberAndPageOrder()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Constraint project");
        string sourcePath = CreateSourceFolder("Constraint source");
        WriteImage(sourcePath, "1.png", PngBytes);
        ChapterImportResult result = await ImportLocalAsync(
            workspace, new ChapterNumber(6), null, sourcePath,
            cancellationToken: TestContext.Current.CancellationToken);
        string databasePath = workspace.DatabasePath;
        string timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(databasePath,
            $"INSERT INTO Chapters (Id, ProjectId, Number, CreatedAt, UpdatedAt) VALUES ('{Guid.NewGuid()}', '{workspace.Project.Id}', '6', '{timestamp}', '{timestamp}');"));
        Page page = result.Pages.Single().Page;
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(databasePath,
            $"INSERT INTO Pages (Id, ChapterId, Number, OriginalFileName, RelativePath, ContentHash) VALUES ('{Guid.NewGuid()}', '{page.ChapterId}', 1, 'other.png', 'other.png', '{page.ContentHash}');"));
    }

    [Fact]
    public async Task ImportAsync_ShouldUseSourceIndependentImageStreams()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Stream source project");
        var source = new ChapterSource(
            [new ChapterSourceImage(
                "remote-page.png",
                ".png",
                _ => ValueTask.FromResult<Stream>(new NonSeekableReadStream(PngBytes)))],
            []);

        ChapterImportResult result = await CreateChapterService().ImportAsync(
            workspace,
            new ImportChapterRequest(new ChapterNumber(7), null, source),
            cancellationToken: TestContext.Current.CancellationToken);

        PageWorkspace page = Assert.Single(result.Pages);
        Assert.Equal("remote-page.png", page.Page.OriginalFileName);
        Assert.Equal(PngBytes, await File.ReadAllBytesAsync(page.FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_ShouldAdoptCandidateFolderUnderChapters_PreservingExistingFilesAndStagingRawSubfolder()
    {
        ProjectWorkspace workspace = await CreateWorkspaceAsync("Project Candidate Test");
        string chaptersDir = Path.Combine(workspace.SourceRoot, "chapters");
        string candidateFolder = Path.Combine(chaptersDir, "001");
        Directory.CreateDirectory(candidateFolder);

        WriteImage(candidateFolder, "01.png", PngBytes);
        WriteImage(candidateFolder, "02.jpg", MinimalJpegBytes);
        string extraFile = Path.Combine(candidateFolder, "user_notes.txt");
        await File.WriteAllTextAsync(extraFile, "keep this file intact", TestContext.Current.CancellationToken);

        ChapterImportResult result = await ImportLocalAsync(
            workspace, new ChapterNumber(1), "Chapter 1", candidateFolder,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Pages.Count);
        string expectedRawFolder = Path.Combine(candidateFolder, "001_raw");
        Assert.True(Directory.Exists(expectedRawFolder));
        Assert.True(File.Exists(Path.Combine(expectedRawFolder, "001.png")));
        Assert.True(File.Exists(Path.Combine(expectedRawFolder, "002.jpg")));
        // Original files and extra user files in candidateFolder are preserved
        Assert.True(File.Exists(Path.Combine(candidateFolder, "01.png")));
        Assert.True(File.Exists(Path.Combine(candidateFolder, "02.jpg")));
        Assert.True(File.Exists(extraFile));
        Assert.Equal("keep this file intact", await File.ReadAllTextAsync(extraFile, TestContext.Current.CancellationToken));
    }

    public void Dispose() => _temporaryWorkspace.Dispose();

    private async Task<ProjectWorkspace> CreateWorkspaceAsync(string name)
    {
        string rootPath = _temporaryWorkspace.GetPath(name);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        var project = new TranslationProject(Guid.NewGuid(), name, "Series 日本語", timestamp, timestamp);
        return await _projectStore.CreateAsync(project, rootPath, TestContext.Current.CancellationToken);
    }

    private string CreateSourceFolder(string name)
    {
        string path = _temporaryWorkspace.GetPath(name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static ChapterService CreateChapterService()
        => new(new FileSystemChapterStore(), TimeProvider.System);

    private static async Task<ChapterImportResult> ImportLocalAsync(
        ProjectWorkspace workspace,
        ChapterNumber number,
        string? title,
        string sourcePath,
        IProgress<ChapterImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CancellationToken effectiveToken = cancellationToken == default
            ? TestContext.Current.CancellationToken
            : cancellationToken;
        ChapterSource source = await new LocalFolderChapterSourceProvider().LoadAsync(
            sourcePath, workspace.SourceRoot, effectiveToken);
        return await CreateChapterService().ImportAsync(
            workspace, new ImportChapterRequest(number, title, source), progress, effectiveToken);
    }

    private static void WriteImage(string folder, string name, byte[] content)
        => File.WriteAllBytes(Path.Combine(folder, name), content);

    private static FileState[] CaptureFiles(string rootPath)
        => [.. Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileState(
                Path.GetRelativePath(rootPath, path),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)];

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> TableExistsAsync(string databasePath, string tableName)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)) == 1;
    }

    private sealed record FileState(string RelativePath, string Hash);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
