using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Projects;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class FileSystemProjectStoreTests : IDisposable
{
    private readonly TemporaryWorkspace _temporaryWorkspace = new();

    [Fact]
    public async Task CreateAsync_ShouldCreateCompleteWorkspace_WhenDestinationDoesNotExist()
    {
        string rootPath = _temporaryWorkspace.GetPath("New project");
        TranslationProject project = CreateProject();

        ProjectWorkspace workspace = await new FileSystemProjectStore().CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(rootPath), workspace.RootPath);
        Assert.True(File.Exists(Path.Combine(rootPath, "project.db")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(rootPath, "chapters")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(rootPath, "cache")));

        foreach (string fileName in new[] { "series.json", "characters.json", "glossary.json", "translation_rules.json" })
        {
            await using FileStream stream = File.OpenRead(Path.Combine(rootPath, "context", fileName));
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
        await new FileSystemProjectStore().CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, reopened.Project.Id);
        Assert.Equal(project.Name, reopened.Project.Name);
        Assert.Equal(project.SeriesName, reopened.Project.SeriesName);
        Assert.Equal(project.CreatedAt, reopened.Project.CreatedAt);
        Assert.Equal(project.UpdatedAt, reopened.Project.UpdatedAt);
        Assert.Equal(rootPath, reopened.RootPath);
    }

    [Fact]
    public async Task CreateAsync_ShouldInitializeSelectedDirectory_WhenItIsEmpty()
    {
        string rootPath = _temporaryWorkspace.GetPath("Empty destination");
        Directory.CreateDirectory(rootPath);

        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken);
        Assert.Equal(rootPath, reopened.RootPath);
    }

    [Theory]
    [InlineData("Project with spaces", "A series with spaces")]
    [InlineData("Proyecto 日本語 한글", "El dragón 日本語 한글")]
    public async Task CreateAsync_ShouldRoundTripNamesAndPaths_WhenTheyContainSpacesOrUnicode(string directoryName, string seriesName)
    {
        string rootPath = _temporaryWorkspace.GetPath(directoryName);
        TranslationProject project = CreateProject(directoryName, seriesName);
        await new FileSystemProjectStore().CreateAsync(project, rootPath, TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(directoryName, reopened.Project.Name);
        Assert.Equal(seriesName, reopened.Project.SeriesName);
        Assert.Equal(rootPath, reopened.RootPath);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectExistingProjectWithoutChangingFiles_WhenDestinationIsAlreadyInitialized()
    {
        string rootPath = _temporaryWorkspace.GetPath("Existing project");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().CreateAsync(CreateProject("Replacement"), rootPath, TestContext.Current.CancellationToken));

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
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.DirectoryNotEmpty, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
        Assert.Single(Directory.EnumerateFileSystemEntries(rootPath));
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectRelativePath_WithoutCreatingProject()
    {
        string absolutePath = _temporaryWorkspace.GetPath("Relative destination");
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absolutePath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().CreateAsync(CreateProject(), relativePath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.False(Directory.Exists(absolutePath));
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectTraversalSegments_WithoutCreatingProject()
    {
        string rootPath = Path.Combine(_temporaryWorkspace.RootPath, "nested", "..", "Traversal destination");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temporaryWorkspace.RootPath));
    }

    [Fact]
    public async Task CreateAsync_ShouldNotCreateFiles_WhenCanceledBeforeStarting()
    {
        string rootPath = _temporaryWorkspace.GetPath("Canceled destination");
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, cancellationSource.Token));

        Assert.False(Directory.Exists(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldUseCurrentLocation_WhenProjectIsRelocated()
    {
        string originalPath = _temporaryWorkspace.GetPath("Original location");
        string relocatedPath = _temporaryWorkspace.GetPath("Relocated project");
        TranslationProject project = CreateProject();
        await new FileSystemProjectStore().CreateAsync(project, originalPath, TestContext.Current.CancellationToken);
        Directory.Move(originalPath, relocatedPath);

        ProjectWorkspace reopened = await new FileSystemProjectStore().OpenAsync(relocatedPath, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, reopened.Project.Id);
        Assert.Equal(relocatedPath, reopened.RootPath);
        Assert.False(Directory.Exists(originalPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldLeaveProjectFilesUnchanged_WhenProjectIsValid()
    {
        string rootPath = _temporaryWorkspace.GetPath("Read only open");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(rootPath);

        await new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken);

        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldReportNotFound_WhenDirectoryDoesNotExist()
    {
        string rootPath = _temporaryWorkspace.GetPath("Missing project");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.NotFound, exception.Code);
        Assert.False(Directory.Exists(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldExplainImagesFoundWhenOpeningRawFolder()
    {
        string rawFolder = _temporaryWorkspace.GetPath("Raw images folder");
        Directory.CreateDirectory(rawFolder);
        await File.WriteAllBytesAsync(
            Path.Combine(rawFolder, "page_01.png"),
            [137, 80, 78, 71, 13, 10, 26, 10],
            TestContext.Current.CancellationToken);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rawFolder, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Contains("contains comic images", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import Chapter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("chapters")]
    [InlineData("cache")]
    public async Task OpenAsync_ShouldRejectIncompleteStructure_WhenRequiredDirectoryIsMissing(string directoryName)
    {
        string rootPath = _temporaryWorkspace.GetPath("Incomplete project");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        Directory.Delete(Path.Combine(rootPath, directoryName));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.False(Directory.Exists(Path.Combine(rootPath, directoryName)));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectIncompleteContext_WhenJsonFileIsMissing()
    {
        string rootPath = _temporaryWorkspace.GetPath("Missing context");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        string contextPath = Path.Combine(rootPath, "context", "characters.json");
        File.Delete(contextPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.False(File.Exists(contextPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldPreserveInvalidDatabase_WhenDatabaseIsNotSqlite()
    {
        string rootPath = _temporaryWorkspace.GetPath("Corrupted database");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "project.db"), "This is not a SQLite database.", TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldPreserveInvalidJson_WhenContextCannotBeDeserialized()
    {
        string rootPath = _temporaryWorkspace.GetPath("Corrupted context");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "context", "glossary.json"), "{ invalid json", TestContext.Current.CancellationToken);
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldReportIncompatibleVersion_WhenContextHasFutureVersion()
    {
        string rootPath = _temporaryWorkspace.GetPath("Future context");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ChangeJsonPropertyAsync(rootPath, "translation_rules.json", "formatVersion", JsonValue.Create(99));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.IncompatibleVersion, exception.Code);
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectMismatchedContext_WhenProjectIdentityDoesNotMatch()
    {
        string rootPath = _temporaryWorkspace.GetPath("Mismatched context");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ChangeJsonPropertyAsync(rootPath, "characters.json", "projectId", JsonValue.Create(Guid.NewGuid()));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
    }

    [Fact]
    public async Task OpenAsync_ShouldReportIncompatibleVersion_WhenDatabaseHasFutureVersion()
    {
        string rootPath = _temporaryWorkspace.GetPath("Future database");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(rootPath, "UPDATE Projects SET FormatVersion = 99;");
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.IncompatibleVersion, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectInvalidMetadata_WhenProjectNameIsBlank()
    {
        string rootPath = _temporaryWorkspace.GetPath("Invalid metadata");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(rootPath, "UPDATE Projects SET Name = '';");

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
    }

    [Theory]
    [InlineData("UPDATE Projects SET FormatVersion = 2147483648;")]
    [InlineData("UPDATE Projects SET CreatedAt = 'not a date';")]
    public async Task OpenAsync_ShouldPreserveDatabase_WhenStoredValuesCannotBeMaterialized(string sql)
    {
        string rootPath = _temporaryWorkspace.GetPath("Malformed storage values");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(rootPath, sql);
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectUnrecognizedSchema_WhenInitialMigrationIsNotRecorded()
    {
        string rootPath = _temporaryWorkspace.GetPath("Missing migration history");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(rootPath, "DELETE FROM __EFMigrationsHistory;");
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
    }

    [Fact]
    public async Task OpenAsync_ShouldRejectUnrecognizedDatabase_WhenProjectTableIsMissing()
    {
        string rootPath = _temporaryWorkspace.GetPath("Unrecognized database");
        await new FileSystemProjectStore().CreateAsync(CreateProject(), rootPath, TestContext.Current.CancellationToken);
        await ExecuteSqlAsync(rootPath, "ALTER TABLE Projects RENAME TO UnrelatedRecords;");
        FileSnapshot[] before = CaptureFiles(rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(
            () => new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidProject, exception.Code);
        Assert.Equal(before, CaptureFiles(rootPath));
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
        Assert.Contains(error.Code, new[] { ProjectErrorCode.AlreadyExists, ProjectErrorCode.DirectoryNotEmpty });
        ProjectWorkspace createdWorkspace = Assert.IsType<ProjectWorkspace>(successfulAttempt.Workspace);
        ProjectWorkspace reopened = await new FileSystemProjectStore().OpenAsync(rootPath, TestContext.Current.CancellationToken);
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
        return [.. Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileSnapshot(Path.GetRelativePath(rootPath, path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.Ordinal)];
    }

    private static async Task ChangeJsonPropertyAsync(string rootPath, string fileName, string propertyName, JsonNode? value)
    {
        string path = Path.Combine(rootPath, "context", fileName);
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        JsonObject document = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        document[propertyName] = value;
        await File.WriteAllTextAsync(path, document.ToJsonString(), TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteSqlAsync(string rootPath, string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(rootPath, "project.db"),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<CreationAttempt> TryCreateAsync(TranslationProject project, string rootPath)
    {
        try
        {
            ProjectWorkspace workspace = await new FileSystemProjectStore().CreateAsync(project, rootPath, TestContext.Current.CancellationToken);
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
