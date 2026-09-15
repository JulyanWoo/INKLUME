using System.Text.Json;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Persistence;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.TextRegions;
using Inklume.Infrastructure.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class LegacyMigrationTests : IDisposable
{
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;
    private readonly IWorkspaceRegistry _registry;

    public LegacyMigrationTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _registry = new JsonWorkspaceRegistry(_dataLocation);
        _store = new FileSystemProjectStore(_dataLocation, _registry);
    }

    [Fact]
    public async Task LegacyProject_ShouldMigrateToExternalDataRoot_AndPreserveLegacyFiles()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("LegacySeries");
        Directory.CreateDirectory(sourceRoot);

        Guid projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var project = new TranslationProject(projectId, "Legacy Manga", "Legacy Series", now, now);

        await SetupLegacyProjectAsync(sourceRoot, project, TestContext.Current.CancellationToken);
        string legacyDbPath = Path.Combine(sourceRoot, "project.db");
        string legacyContextDir = Path.Combine(sourceRoot, "context");

        byte[] legacyDbBefore = await File.ReadAllBytesAsync(legacyDbPath, TestContext.Current.CancellationToken);

        // Open legacy project
        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // 1. Verify workspace points to external DataRoot
        Assert.Equal(sourceRoot, workspace.SourceRoot);
        Assert.Equal(_dataLocation.GetDataRoot(projectId), workspace.DataRoot);
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.True(File.Exists(Path.Combine(workspace.DataRoot, "workspace.json")));

        // 2. Verify legacy files in sourceRoot are untouched and NOT deleted
        Assert.True(File.Exists(legacyDbPath));
        Assert.True(Directory.Exists(legacyContextDir));
        byte[] legacyDbAfter = await File.ReadAllBytesAsync(legacyDbPath, TestContext.Current.CancellationToken);
        Assert.Equal(legacyDbBefore, legacyDbAfter);

        // 3. Verify registry recorded the migration
        WorkspaceRegistryEntry? entry = await _registry.FindBySourceRootAsync(sourceRoot, TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.True(entry.IsLegacyMigrated);
        Assert.Equal(projectId, entry.ProjectId);
    }

    [Fact]
    public async Task LookupOrder_MigratedLegacyFolder_NeverReEntersLegacyMigration()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("IdempotentLegacySeries");
        Directory.CreateDirectory(sourceRoot);

        Guid projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var project = new TranslationProject(projectId, "Original Legacy Name", "Legacy Series", now, now);

        await SetupLegacyProjectAsync(sourceRoot, project, TestContext.Current.CancellationToken);

        // First open: migrates legacy project
        ProjectWorkspace firstOpen = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);
        Assert.Equal("Original Legacy Name", firstOpen.Project.Name);

        // Modify the EXTERNAL database to simulate new work done in INKLUME
        await ExecuteSqlAsync(firstOpen.DatabasePath, "UPDATE Projects SET Name = 'Updated in External DataRoot';");

        // Second open: MUST load from external DataRoot via WorkspaceRegistry (Order: 1. Registry -> existing external workspace)
        // It must NOT re-migrate from the legacy project.db (which still has 'Original Legacy Name')
        ProjectWorkspace secondOpen = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);
        Assert.Equal("Updated in External DataRoot", secondOpen.Project.Name);
    }

    [Fact]
    public async Task LegacyMigration_ShouldPreserveTextRegionsAndChapters()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("LegacyWithRegions");
        Directory.CreateDirectory(sourceRoot);

        Guid projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var project = new TranslationProject(projectId, "Region Legacy Manga", "Series", now, now);

        await SetupLegacyProjectAsync(sourceRoot, project, TestContext.Current.CancellationToken);
        string legacyDbPath = Path.Combine(sourceRoot, "project.db");

        // Add a chapter, page, and text region directly into the legacy database
        Guid chapterId = Guid.NewGuid();
        Guid pageId = Guid.NewGuid();
        Guid regionId = Guid.NewGuid();
        string timestamp = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await ExecuteSqlAsync(legacyDbPath,
            $"INSERT INTO Chapters (Id, ProjectId, Number, Title, CreatedAt, UpdatedAt) VALUES ('{chapterId.ToString().ToUpperInvariant()}', '{projectId.ToString().ToUpperInvariant()}', 1.0, 'Chapter 1', '{timestamp}', '{timestamp}');");
        await ExecuteSqlAsync(legacyDbPath,
            $"INSERT INTO Pages (Id, ChapterId, Number, OriginalFileName, RelativePath, ContentHash, PixelWidth, PixelHeight) VALUES ('{pageId.ToString().ToUpperInvariant()}', '{chapterId.ToString().ToUpperInvariant()}', 1, '01.png', '01.png', '{new string('0', 64)}', 800, 1200);");
        await ExecuteSqlAsync(legacyDbPath,
            $"INSERT INTO TextRegions (Id, PageId, GeometryKind, ReadingOrder, Role, ContainerType, CreatedAt, UpdatedAt) VALUES ('{regionId.ToString().ToUpperInvariant()}', '{pageId.ToString().ToUpperInvariant()}', 1, 1, 1, 2, '{timestamp}', '{timestamp}');");
        await ExecuteSqlAsync(legacyDbPath,
            $"INSERT INTO TextRegionPoints (TextRegionId, PointIndex, X, Y) VALUES " +
            $"('{regionId.ToString().ToUpperInvariant()}', 0, 0.1, 0.1), " +
            $"('{regionId.ToString().ToUpperInvariant()}', 1, 0.4, 0.1), " +
            $"('{regionId.ToString().ToUpperInvariant()}', 2, 0.4, 0.5), " +
            $"('{regionId.ToString().ToUpperInvariant()}', 3, 0.1, 0.5);");

        // Migrate by opening
        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // Verify entities survived migration in external workspace
        var textRegionStore = new SqliteTextRegionStore();
        IReadOnlyList<TextRegion> regions = await textRegionStore.GetForPageAsync(workspace, pageId, TestContext.Current.CancellationToken);

        TextRegion region = Assert.Single(regions);
        Assert.Equal(regionId, region.Id);
        Assert.Equal(1, region.ReadingOrder);
        Assert.Equal(TextRegionRole.Dialogue, region.Role);
        Assert.Equal(TextContainerType.SpeechBubble, region.ContainerType);
        Assert.Equal(4, region.Geometry.Points.Count);
        Assert.Equal(0.1, region.Geometry.Bounds.X, precision: 4);
        Assert.Equal(0.1, region.Geometry.Bounds.Y, precision: 4);
        Assert.Equal(0.3, region.Geometry.Bounds.Width, precision: 4);
        Assert.Equal(0.4, region.Geometry.Bounds.Height, precision: 4);
    }

    public void Dispose() => _temporaryWorkspace.Dispose();

    private static async Task SetupLegacyProjectAsync(string sourceRoot, TranslationProject project, CancellationToken cancellationToken)
    {
        string legacyDbPath = Path.Combine(sourceRoot, "project.db");
        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = legacyDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString())
            .Options;
        await using var context = new ProjectDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        string timestamp = project.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteSqlAsync(legacyDbPath,
            $"INSERT INTO Projects (Id, Name, SeriesName, FormatVersion, CreatedAt, UpdatedAt) VALUES ('{project.Id.ToString().ToUpperInvariant()}', '{project.Name}', '{project.SeriesName}', 1, '{timestamp}', '{timestamp}');");

        string legacyContextDir = Path.Combine(sourceRoot, "context");
        Directory.CreateDirectory(legacyContextDir);

        var seriesDoc = new { formatVersion = 1, projectId = project.Id, name = project.SeriesName };
        var headerDoc = new { formatVersion = 1, projectId = project.Id };

        await File.WriteAllTextAsync(Path.Combine(legacyContextDir, "series.json"), JsonSerializer.Serialize(seriesDoc), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(legacyContextDir, "characters.json"), JsonSerializer.Serialize(headerDoc), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(legacyContextDir, "glossary.json"), JsonSerializer.Serialize(headerDoc), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(legacyContextDir, "translation_rules.json"), JsonSerializer.Serialize(headerDoc), cancellationToken);
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
}
