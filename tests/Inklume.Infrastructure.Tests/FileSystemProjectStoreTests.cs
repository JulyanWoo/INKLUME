using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class FileSystemProjectStoreTests : IDisposable
{
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;

    public FileSystemProjectStoreTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _store = new FileSystemProjectStore(_dataLocation);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateCompleteWorkspace_WhenDestinationDoesNotExist()
    {
        string rootPath = _temporaryWorkspace.GetPath("New project");
        TranslationProject project = CreateProject();

        ProjectWorkspace workspace = await _store.CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(rootPath), workspace.SourceRoot);
        // Source root is user content and must NOT contain project.db or workspace.db
        Assert.False(File.Exists(Path.Combine(rootPath, "project.db")));
        Assert.False(File.Exists(Path.Combine(rootPath, "workspace.db")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(rootPath));

        // Data root must contain workspace.db, context, and cache
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.True(Directory.Exists(workspace.ContextRoot));
        Assert.True(Directory.Exists(workspace.CacheRoot));

        foreach (string fileName in new[] { "series.json", "characters.json", "glossary.json", "translation_rules.json" })
        {
            await using FileStream stream = File.OpenRead(Path.Combine(workspace.ContextRoot, fileName));
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal(project.Id, document.RootElement.GetProperty("projectId").GetGuid());
            if (fileName == "series.json")
            {
                Assert.Equal(project.SeriesName, document.RootElement.GetProperty("name").GetString());
            }
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistIdentityAndMetadata_WhenOpenedWithNewStore()
    {
        string rootPath = _temporaryWorkspace.GetPath("Persisted project");
        TranslationProject project = CreateProject();
        await _store.CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        var secondStore = new FileSystemProjectStore(_dataLocation);
        ProjectWorkspace reopened = await secondStore.OpenAsync(rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, reopened.Project.Id);
        Assert.Equal(project.Name, reopened.Project.Name);
        Assert.Equal(project.SeriesName, reopened.Project.SeriesName);
        Assert.Equal(project.CreatedAt, reopened.Project.CreatedAt);
        Assert.Equal(project.UpdatedAt, reopened.Project.UpdatedAt);
        Assert.Equal(rootPath, reopened.SourceRoot);
    }

    [Fact]
    public async Task CreateAsync_ShouldInitializeSelectedDirectory_WhenItIsEmpty()
    {
        string rootPath = _temporaryWorkspace.GetPath("Empty destination");
        Directory.CreateDirectory(rootPath);

        await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await _store.OpenAsync(rootPath, TestContext.Current.CancellationToken);
        Assert.Equal(rootPath, reopened.SourceRoot);
    }

    [Theory]
    [InlineData("Project with spaces", "A series with spaces")]
    [InlineData("Proyecto 日本語 한글", "El dragón 日本語 한글")]
    public async Task CreateAsync_ShouldRoundTripNamesAndPaths_WhenTheyContainSpacesOrUnicode(string directoryName, string seriesName)
    {
        string rootPath = _temporaryWorkspace.GetPath(directoryName);
        TranslationProject project = CreateProject(directoryName, seriesName);
        await _store.CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await _store.OpenAsync(rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(directoryName, reopened.Project.Name);
        Assert.Equal(seriesName, reopened.Project.SeriesName);
        Assert.Equal(rootPath, reopened.SourceRoot);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectExistingProjectWithoutChangingFiles_WhenDestinationIsAlreadyInitialized()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing project");
        await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.CreateAsync(CreateProject("Replacement"), rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.AlreadyExists, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveUnrelatedFiles_WhenDestinationIsNotEmpty()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing documents");
        Directory.CreateDirectory(rootPath);
        string originalPath = Path.Combine(rootPath, "original.txt");
        await File.WriteAllTextAsync(originalPath, "Preserve this original document.", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.CreateAsync(
            CreateProject(), rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(rootPath), workspace.SourceRoot);
        Assert.True(File.Exists(originalPath));
        Assert.Equal("Preserve this original document.", await File.ReadAllTextAsync(originalPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(rootPath, "project.db")));
        Assert.False(File.Exists(Path.Combine(rootPath, "workspace.db")));
        Assert.True(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveUnrelatedFolder_WhenDestinationHasUserFolders()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing references");
        string refDir = Path.Combine(rootPath, "references");
        Directory.CreateDirectory(refDir);
        string refFile = Path.Combine(refDir, "notes.txt");
        await File.WriteAllTextAsync(refFile, "Reference material", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.CreateAsync(
            CreateProject(), rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(rootPath), workspace.SourceRoot);
        Assert.True(Directory.Exists(refDir));
        Assert.True(File.Exists(refFile));
        Assert.Equal("Reference material", await File.ReadAllTextAsync(refFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_ShouldPreservePreExistingChaptersDirectory_WhenItAlreadyExists()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing chapters");
        string chaptersDir = Path.Combine(rootPath, "chapters");
        Directory.CreateDirectory(chaptersDir);
        string manualChapter = Path.Combine(chaptersDir, "chapter_manual_01");
        Directory.CreateDirectory(manualChapter);
        string noteFile = Path.Combine(manualChapter, "note.txt");
        await File.WriteAllTextAsync(noteFile, "Manual chapter note", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.CreateAsync(
            CreateProject(), rootPath, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(chaptersDir));
        Assert.True(Directory.Exists(manualChapter));
        Assert.True(File.Exists(noteFile));
        Assert.Equal("Manual chapter note", await File.ReadAllTextAsync(noteFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_ShouldPreservePreExistingContextFiles_WhenCompatible()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing context");
        string contextDir = Path.Combine(rootPath, "context");
        Directory.CreateDirectory(contextDir);
        string customFile = Path.Combine(contextDir, "custom_notes.txt");
        await File.WriteAllTextAsync(customFile, "Context notes", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.CreateAsync(
            CreateProject(), rootPath, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(customFile));
        Assert.Equal("Context notes", await File.ReadAllTextAsync(customFile, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(workspace.ContextRoot, "series.json")));
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectRelativePath_WithoutCreatingProject()
    {
        string absolutePath = _temporaryWorkspace.GetPath("Relative destination");
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absolutePath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.CreateAsync(CreateProject(), relativePath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.False(Directory.Exists(absolutePath));
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectTraversalSegments_WithoutCreatingProject()
    {
        string sourceRoot = Path.Combine(_temporaryWorkspace.RootPath, "nested", "..", "Traversal destination");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.CreateAsync(CreateProject(), sourceRoot, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.False(Directory.Exists(sourceRoot));
    }

    [Fact]
    public async Task CreateAsync_ShouldNotCreateFiles_WhenCanceledBeforeStarting()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("Canceled destination");
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.CreateAsync(CreateProject(), sourceRoot, cancellationSource.Token));

        Assert.False(Directory.Exists(sourceRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldLeaveProjectFilesUnchanged_WhenProjectIsValid()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("Read only open");
        await _store.CreateAsync(CreateProject(), sourceRoot, TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(sourceRoot);

        await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        Assert.Equal(before, CaptureFiles(sourceRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldReportNotFound_WhenDirectoryDoesNotExist()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("Missing project");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.NotFound, exception.Code);
        Assert.False(Directory.Exists(sourceRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldSucceedAndLeaveFolderUntouched_WhenOpeningRawFolderWithImages()
    {
        string rawFolder = _temporaryWorkspace.GetPath("Raw images folder");
        Directory.CreateDirectory(rawFolder);
        string imagePath = Path.Combine(rawFolder, "page_01.png");
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10];
        await File.WriteAllBytesAsync(imagePath, imageBytes, TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.OpenAsync(rawFolder, TestContext.Current.CancellationToken);

        Assert.Equal(rawFolder, workspace.SourceRoot);
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.False(File.Exists(Path.Combine(rawFolder, "project.db")));
        Assert.False(File.Exists(Path.Combine(rawFolder, "workspace.db")));
        Assert.Equal(imageBytes, await File.ReadAllBytesAsync(imagePath, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(rawFolder));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectIncompleteStructure_WhenRequiredDirectoryIsMissing()
    {
        string rootPath = _temporaryWorkspace.GetPath("Incomplete project");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        Directory.Delete(workspace.ContextRoot, recursive: true);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.False(Directory.Exists(workspace.ContextRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectIncompleteContext_WhenJsonFileIsMissing()
    {
        string rootPath = _temporaryWorkspace.GetPath("Missing context");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        string contextFilePath = Path.Combine(workspace.ContextRoot, "characters.json");
        File.Delete(contextFilePath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.False(File.Exists(contextFilePath));
    }

    [Fact]
    public async Task OpenAsync_ShouldPreserveInvalidDatabase_WhenDatabaseIsNotSqlite()
    {
        string rootPath = _temporaryWorkspace.GetPath("Corrupted database");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(workspace.DatabasePath, "This is not a SQLite database.", TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldPreserveInvalidJson_WhenContextCannotBeDeserialized()
    {
        string rootPath = _temporaryWorkspace.GetPath("Corrupted context");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(workspace.ContextRoot, "glossary.json"), "{ invalid json", TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldReportIncompatibleVersion_WhenContextHasFutureVersion()
    {
        string rootPath = _temporaryWorkspace.GetPath("Future context");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ChangeJsonPropertyAsync(workspace.ContextRoot, "translation_rules.json", "formatVersion", JsonValue.Create(99));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.IncompatibleVersion, exception.Code);
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectMismatchedContext_WhenProjectIdentityDoesNotMatch()
    {
        string rootPath = _temporaryWorkspace.GetPath("Mismatched context");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ChangeJsonPropertyAsync(workspace.ContextRoot, "characters.json", "projectId", JsonValue.Create(Guid.NewGuid()));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
    }

    [Fact]
    public async Task OpenAsync_ShouldReportIncompatibleVersion_WhenDatabaseHasFutureVersion()
    {
        string rootPath = _temporaryWorkspace.GetPath("Future database");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(workspace.DatabasePath, "UPDATE Projects SET FormatVersion = 99;");
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.IncompatibleVersion, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectInvalidMetadata_WhenProjectNameIsBlank()
    {
        string rootPath = _temporaryWorkspace.GetPath("Invalid metadata");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(workspace.DatabasePath, "UPDATE Projects SET Name = '';");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
    }

    [Theory]
    [InlineData("UPDATE Projects SET FormatVersion = 2147483648;")]
    [InlineData("UPDATE Projects SET CreatedAt = 'not a date';")]
    public async Task OpenAsync_ShouldPreserveDatabase_WhenStoredValuesCannotBeMaterialized(string sql)
    {
        string rootPath = _temporaryWorkspace.GetPath("Malformed storage values");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(workspace.DatabasePath, sql);
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectUnrecognizedSchema_WhenInitialMigrationIsNotRecorded()
    {
        string rootPath = _temporaryWorkspace.GetPath("Missing migration history");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(workspace.DatabasePath, "DELETE FROM __EFMigrationsHistory;");
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectUnrecognizedDatabase_WhenProjectTableIsMissing()
    {
        string rootPath = _temporaryWorkspace.GetPath("Unrecognized database");
        ProjectWorkspace workspace = await _store.CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(workspace.DatabasePath, "ALTER TABLE Projects RENAME TO UnrelatedRecords;");
        FileSnapshot[] before = CaptureFiles(workspace.DataRoot);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => _store.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(workspace.DataRoot));
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveSingleProject_WhenTwoStoresUseSameDestination()
    {
        string rootPath = _temporaryWorkspace.GetPath("Concurrent destination");

        CreationAttempt[] attempts = await Task.WhenAll(
            TryCreateAsync(CreateProject("First project"), rootPath),
            TryCreateAsync(CreateProject("Second project"), rootPath));

        CreationAttempt successfulAttempt = Assert.Single(attempts, attempt => attempt.Workspace is not null);
        CreationAttempt rejectedAttempt = Assert.Single(attempts, attempt => attempt.Error is not null);
        ProjectOperationException error = Assert.IsType<ProjectOperationException>(rejectedAttempt.Error);
        Assert.Equal(ProjectErrorCode.AlreadyExists, error.Code);
        ProjectWorkspace createdWorkspace = Assert.IsType<ProjectWorkspace>(successfulAttempt.Workspace);
        ProjectWorkspace reopened = await _store.OpenAsync(rootPath, TestContext.Current.CancellationToken);
        Assert.Equal(createdWorkspace.Project.Id, reopened.Project.Id);
        Assert.Equal(createdWorkspace.Project.Name, reopened.Project.Name);
    }

    public void Dispose() => _temporaryWorkspace.Dispose();

    private static TranslationProject CreateProject(string name = "Localization project", string seriesName = "The First Series")
    {
        var createdAt = new DateTimeOffset(2026, 9, 13, 12, 30, 0, TimeSpan.FromHours(-5));
        return new TranslationProject(Guid.NewGuid(), name, seriesName, createdAt, createdAt.AddMinutes(5));
    }

    private static FileSnapshot[] CaptureFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return [];
        return [.. Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileSnapshot(Path.GetRelativePath(rootPath, path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.Ordinal)];
    }

    private static async Task ChangeJsonPropertyAsync(string contextRoot, string fileName, string propertyName, JsonNode? value)
    {
        string path = Path.Combine(contextRoot, fileName);
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        JsonObject document = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        document[propertyName] = value;
        await File.WriteAllTextAsync(path, document.ToJsonString(), TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<CreationAttempt> TryCreateAsync(TranslationProject project, string rootPath)
    {
        try
        {
            ProjectWorkspace workspace = await _store.CreateAsync(project, rootPath, TestContext.Current.CancellationToken);
            return new CreationAttempt(workspace, null);
        }
        catch (ProjectOperationException exception)
        {
            return new CreationAttempt(null, exception);
        }
    }

    private sealed record FileSnapshot(string RelativePath, string Hash);

    private sealed record CreationAttempt(ProjectWorkspace? Workspace, ProjectOperationException? Error);
}
