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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class VisualEditorPersistenceTests : IDisposable
{
    private const string PreviousMigration = "20260913192826_AddChaptersAndPages";
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;

    public VisualEditorPersistenceTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _store = new FileSystemProjectStore(_dataLocation);
    }

    [Fact]
    public async Task PreviousSchema_ShouldMigrateAndLazilyPersistPageDimensions()
    {
        (ProjectWorkspace workspace, PageWorkspace importedPage) = await CreateProjectWithPageAsync("Legacy project", 1);
        string databasePath = workspace.DatabasePath;
        await MigrateToAsync(databasePath, PreviousMigration);

        ProjectWorkspace reopened = await _store.OpenAsync(
            workspace.SourceRoot, TestContext.Current.CancellationToken);
        ChapterService chapterService = CreateChapterService();
        PageWorkspace unresolved = Assert.Single(await chapterService.GetPagesAsync(
            reopened, importedPage.Page.ChapterId, TestContext.Current.CancellationToken));
        PageWorkspace resolved = await chapterService.EnsurePageDimensionsAsync(
            reopened, unresolved.Page.Id, TestContext.Current.CancellationToken);
        PageWorkspace restored = await chapterService.EnsurePageDimensionsAsync(
            reopened, unresolved.Page.Id, TestContext.Current.CancellationToken);

        Assert.False(unresolved.Page.HasDimensions);
        Assert.Equal(1, resolved.Page.PixelWidth);
        Assert.Equal(1, resolved.Page.PixelHeight);
        Assert.Equal(resolved.Page, restored.Page);
        Assert.Contains("20260914145247_AddVisualEditorFoundation", await ReadAppliedMigrationsAsync(databasePath));
    }

    [Fact]
    public async Task TextRegions_ShouldPersistOrderedGeometryClassificationAndPageIsolation()
    {
        (ProjectWorkspace workspace, PageWorkspace firstPage) = await CreateProjectWithPageAsync("Region project", 1);
        PageWorkspace secondPage = (await ImportPageAsync(workspace, 2)).Pages.Single();
        var service = new TextRegionService(new SqliteTextRegionStore(), TimeProvider.System);
        TextRegion rectangle = await service.CreateAsync(
            workspace,
            firstPage.Page.Id,
            new RectangleTextRegionGeometry(0.1, 0.1, 0.2, 0.3),
            TextRegionRole.Dialogue,
            TextContainerType.SpeechBubble,
            TestContext.Current.CancellationToken);
        TextRegion polygon = await service.CreateAsync(
            workspace,
            firstPage.Page.Id,
            new PolygonTextRegionGeometry(
                [new ImagePoint(0.55, 0.1), new ImagePoint(0.9, 0.2), new ImagePoint(0.7, 0.8)]),
            TextRegionRole.Sfx,
            TextContainerType.None,
            TestContext.Current.CancellationToken);

        ProjectWorkspace reopened = await _store.OpenAsync(
            workspace.SourceRoot, TestContext.Current.CancellationToken);
        IReadOnlyList<TextRegion> restored = await service.GetForPageAsync(
            reopened, firstPage.Page.Id, TestContext.Current.CancellationToken);
        IReadOnlyList<TextRegion> otherPageRegions = await service.GetForPageAsync(
            reopened, secondPage.Page.Id, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], restored.Select(region => region.ReadingOrder));
        Assert.Equal(rectangle.Geometry.Points, restored[0].Geometry.Points);
        Assert.Equal(polygon.Geometry.Points, restored[1].Geometry.Points);
        Assert.Equal(TextRegionRole.Dialogue, restored[0].Role);
        Assert.Equal(TextContainerType.None, restored[1].ContainerType);
        Assert.Empty(otherPageRegions);
    }

    [Fact]
    public async Task Database_ShouldEnforceReadingOrderAndCascadeRegionPointsWithPage()
    {
        (ProjectWorkspace workspace, PageWorkspace page) = await CreateProjectWithPageAsync("Constraint project", 1);
        var service = new TextRegionService(new SqliteTextRegionStore(), TimeProvider.System);
        TextRegion region = await service.CreateAsync(
            workspace,
            page.Page.Id,
            new RectangleTextRegionGeometry(0.1, 0.1, 0.5, 0.5),
            cancellationToken: TestContext.Current.CancellationToken);
        string databasePath = workspace.DatabasePath;
        string timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
            databasePath,
            "INSERT INTO TextRegions (Id, PageId, GeometryKind, ReadingOrder, Role, ContainerType, CreatedAt, UpdatedAt) " +
            $"VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', " +
            $"'{page.Page.Id.ToString().ToUpperInvariant()}', 1, 1, 0, 0, '{timestamp}', '{timestamp}');"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(
            databasePath,
            "INSERT INTO TextRegionPoints (TextRegionId, PointIndex, X, Y) " +
            $"VALUES ('{region.Id.ToString().ToUpperInvariant()}', 0, 0.2, 0.2);"));

        await ExecuteSqlAsync(
            databasePath,
            $"DELETE FROM Pages WHERE Id = '{page.Page.Id.ToString().ToUpperInvariant()}';");

        Assert.Equal(0L, await ExecuteScalarAsync(databasePath, "SELECT COUNT(*) FROM TextRegions;"));
        Assert.Equal(0L, await ExecuteScalarAsync(databasePath, "SELECT COUNT(*) FROM TextRegionPoints;"));
    }

    public void Dispose() => _temporaryWorkspace.Dispose();

    private async Task<(ProjectWorkspace Workspace, PageWorkspace Page)> CreateProjectWithPageAsync(
        string name,
        decimal chapterNumber)
    {
        string sourceRoot = _temporaryWorkspace.GetPath(name);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        var project = new TranslationProject(Guid.NewGuid(), name, "Series", timestamp, timestamp);
        ProjectWorkspace workspace = await _store.CreateAsync(
            project, sourceRoot, TestContext.Current.CancellationToken);
        ChapterImportResult imported = await ImportPageAsync(workspace, chapterNumber);
        return (imported.Workspace, imported.Pages.Single());
    }

    private async Task<ChapterImportResult> ImportPageAsync(ProjectWorkspace workspace, decimal chapterNumber)
    {
        string sourcePath = _temporaryWorkspace.GetPath($"source-{chapterNumber}");
        Directory.CreateDirectory(sourcePath);
        string imagePath = Path.Combine(sourcePath, "page.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes, TestContext.Current.CancellationToken);
        ChapterSource source = await new LocalFolderChapterSourceProvider().LoadAsync(
            sourcePath, workspace.SourceRoot, TestContext.Current.CancellationToken);
        return await CreateChapterService().ImportAsync(
            workspace,
            new ImportChapterRequest(new ChapterNumber(chapterNumber), null, source),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static ChapterService CreateChapterService()
        => new(new FileSystemChapterStore(), TimeProvider.System);

    private static async Task MigrateToAsync(string databasePath, string migration)
    {
        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString())
            .Options;
        await using var context = new ProjectDbContext(options);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migration, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString())
            .Options;
        await using var context = new ProjectDbContext(options);
        return [.. await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)];
    }

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using SqliteConnection connection = CreateConnection(databasePath);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> ExecuteScalarAsync(string databasePath, string sql)
    {
        await using SqliteConnection connection = CreateConnection(databasePath);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SqliteConnection CreateConnection(string databasePath)
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
}
