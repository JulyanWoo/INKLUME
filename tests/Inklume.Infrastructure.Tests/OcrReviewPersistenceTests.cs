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

public sealed class OcrReviewPersistenceTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;
    private readonly SqliteTextRegionStore _regionStore = new();
    private readonly SqliteOcrStore _ocrStore = new();

    public OcrReviewPersistenceTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _store = new FileSystemProjectStore(_dataLocation);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temporaryWorkspace.Dispose();
    }

    [Fact]
    public async Task Migration_FromAddOcrFoundation_ShouldApplyCleanly_AndSetDefaultPending()
    {
        string dbPath = Path.Combine(_temporaryWorkspace.RootPath, "migration_test.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(connectionString)
            .Options;

        Guid regionId = Guid.NewGuid();

        // 1. Migrate up to AddOcrFoundation only
        await using (var context = new ProjectDbContext(options))
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("AddOcrFoundation", TestContext.Current.CancellationToken);

            // Insert project, chapter, page, text region, and OCR recognition under Phase 5 schema
            Guid projectId = Guid.NewGuid();
            Guid chapterId = Guid.NewGuid();
            Guid pageId = Guid.NewGuid();
            Guid ocrId = Guid.NewGuid();
            string timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO Projects (Id, Name, SeriesName, FormatVersion, CreatedAt, UpdatedAt) VALUES ('{projectId:D}', 'Test', 'Series', 1, '{timestamp}', '{timestamp}');");
            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO Chapters (Id, ProjectId, Number, Title, CreatedAt, UpdatedAt) VALUES ('{chapterId:D}', '{projectId:D}', 1.0, 'Ch1', '{timestamp}', '{timestamp}');");
            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO Pages (Id, ChapterId, Number, OriginalFileName, RelativePath, ContentHash, PixelWidth, PixelHeight) VALUES ('{pageId:D}', '{chapterId:D}', 1, '01.png', '01.png', '{new string('0', 64)}', 800, 1200);");
            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO TextRegions (Id, PageId, GeometryKind, ReadingOrder, Role, ContainerType, CreatedAt, UpdatedAt, Origin) VALUES ('{regionId:D}', '{pageId:D}', 1, 1, 1, 2, '{timestamp}', '{timestamp}', 1);");
            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO TextRegionPoints (TextRegionId, PointIndex, X, Y) VALUES ('{regionId:D}', 0, 10, 10), ('{regionId:D}', 1, 50, 10), ('{regionId:D}', 2, 50, 40), ('{regionId:D}', 3, 10, 40);");
            await ExecuteSqlAsync(dbPath,
                $"INSERT INTO OcrRecognitions (Id, TextRegionId, Text, RecognitionConfidence, DetectionConfidence, EngineName, EngineVersion, DetectionModel, RecognitionModel, ModelProfile, CreatedAt, UpdatedAt) VALUES ('{ocrId:D}', '{regionId:D}', '원래 OCR 텍스트', 0.95, 0.90, 'PaddleOCR', '3.7.0', 'det', 'rec', 'Korean', '{timestamp}', '{timestamp}');");
        }

        // 2. Migrate to latest (AddOcrReviewFoundation)
        await using (var context = new ProjectDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        // 3. Verify data survived and review defaults applied
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT ReviewStatus, ReviewedText, ReviewedAt, UserModifiedAt FROM TextRegions WHERE Id = '{regionId:D}';";
            await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, reader.GetInt32(0)); // ReviewStatus.Pending = 0
            Assert.True(reader.IsDBNull(1));     // ReviewedText is null
            Assert.True(reader.IsDBNull(2));     // ReviewedAt is null
            Assert.True(reader.IsDBNull(3));     // UserModifiedAt is null
        }

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT Text FROM OcrRecognitions WHERE TextRegionId = '{regionId:D}';";
            var text = (string?)await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            Assert.Equal("원래 OCR 텍스트", text);
        }
    }

    [Fact]
    public async Task ReviewState_PersistenceRoundTrip_ShouldPreserveKoreanMultilineAndNullVsEmpty()
    {
        (ProjectWorkspace workspace, PageWorkspace pageWorkspace) = await CreateProjectWithPageAsync("RoundTrip_Project", 1);
        Page page = pageWorkspace.Page;

        var geom1 = new RectangleTextRegionGeometry(10, 10, 50, 50);
        var geom2 = new RectangleTextRegionGeometry(70, 10, 50, 50);
        var geom3 = new RectangleTextRegionGeometry(10, 70, 50, 50);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset reviewedAt = now.AddMinutes(5);
        DateTimeOffset modifiedAt = now.AddMinutes(6);

        // Region 1: Null reviewed text, Pending
        var r1 = new TextRegion(
            Guid.NewGuid(), page.Id, geom1, 1, TextRegionRole.Unknown, TextContainerType.Unknown,
            now, now, TextRegionOrigin.OcrDetection, null, TextRegionReviewStatus.Pending, null, null);

        // Region 2: Empty string reviewed text, Reviewed
        var r2 = new TextRegion(
            Guid.NewGuid(), page.Id, geom2, 2, TextRegionRole.Dialogue, TextContainerType.SpeechBubble,
            now, now, TextRegionOrigin.OcrDetection, string.Empty, TextRegionReviewStatus.Reviewed, reviewedAt, modifiedAt);

        // Region 3: Korean multiline text with spaces, Reviewed
        string koreanMultiline = "안녕하세요!\n이것은 한국어 줄바꿈 테스트입니다.\r\n   공백과 기호: ?!~";
        var r3 = new TextRegion(
            Guid.NewGuid(), page.Id, geom3, 3, TextRegionRole.Thought, TextContainerType.ThoughtBubble,
            now, now, TextRegionOrigin.Manual, koreanMultiline, TextRegionReviewStatus.Reviewed, reviewedAt, modifiedAt);

        await _regionStore.AddAsync(workspace, r1, TestContext.Current.CancellationToken);
        await _regionStore.AddAsync(workspace, r2, TestContext.Current.CancellationToken);
        await _regionStore.AddAsync(workspace, r3, TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> reloaded = await _regionStore.GetForPageAsync(workspace, page.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, reloaded.Count);

        TextRegion reloaded1 = reloaded.Single(r => r.Id == r1.Id);
        Assert.Null(reloaded1.ReviewedText);
        Assert.Equal(TextRegionReviewStatus.Pending, reloaded1.ReviewStatus);
        Assert.Null(reloaded1.ReviewedAt);
        Assert.Null(reloaded1.UserModifiedAt);

        TextRegion reloaded2 = reloaded.Single(r => r.Id == r2.Id);
        Assert.Equal(string.Empty, reloaded2.ReviewedText);
        Assert.Equal(TextRegionReviewStatus.Reviewed, reloaded2.ReviewStatus);
        Assert.NotNull(reloaded2.ReviewedAt);
        Assert.NotNull(reloaded2.UserModifiedAt);

        TextRegion reloaded3 = reloaded.Single(r => r.Id == r3.Id);
        Assert.Equal(koreanMultiline, reloaded3.ReviewedText);
        Assert.Equal(TextRegionReviewStatus.Reviewed, reloaded3.ReviewStatus);
        Assert.Equal(TextRegionRole.Thought, reloaded3.Role);
        Assert.Equal(TextContainerType.ThoughtBubble, reloaded3.ContainerType);
    }

    [Fact]
    public async Task ReopenWorkspace_ShouldRestoreExactReviewState()
    {
        string projectName = "Reopen_Review_Project";
        (ProjectWorkspace workspace, PageWorkspace pageWorkspace) = await CreateProjectWithPageAsync(projectName, 1);
        Page page = pageWorkspace.Page;

        var geom = new RectangleTextRegionGeometry(10, 10, 50, 50);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var region = new TextRegion(
            Guid.NewGuid(), page.Id, geom, 1, TextRegionRole.Dialogue, TextContainerType.SpeechBubble,
            now, now, TextRegionOrigin.OcrDetection, "수정된 텍스트", TextRegionReviewStatus.Reviewed, now, now);

        await _regionStore.AddAsync(workspace, region, TestContext.Current.CancellationToken);

        // Attach OCR recognition
        var engine = new OcrEngineMetadata("PaddleOCR", "3.7.0", "det", "rec", "Korean");
        await _ocrStore.SaveRegionRecognitionAsync(
            workspace, region.Id, new OcrRegionResult("원본 OCR", 0.93, engine), TestContext.Current.CancellationToken);

        // Close and reopen workspace from source root
        SqliteConnection.ClearAllPools();
        ProjectWorkspace reopened = await _store.OpenAsync(workspace.SourceRoot, TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> regions = await _regionStore.GetForPageAsync(reopened, page.Id, TestContext.Current.CancellationToken);
        TextRegion restoredRegion = Assert.Single(regions);

        Assert.Equal("수정된 텍스트", restoredRegion.ReviewedText);
        Assert.Equal(TextRegionReviewStatus.Reviewed, restoredRegion.ReviewStatus);
        Assert.Equal(TextRegionRole.Dialogue, restoredRegion.Role);
        Assert.Equal(TextContainerType.SpeechBubble, restoredRegion.ContainerType);

        OcrRecognition? ocr = await _ocrStore.GetRecognitionForRegionAsync(reopened, region.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(ocr);
        Assert.Equal("원본 OCR", ocr.Text);
    }

    [Fact]
    public async Task ReorderPageRegionsAsync_SafeTwoPhaseTransaction_ShouldSwapWithoutCollision()
    {
        (ProjectWorkspace workspace, PageWorkspace pageWorkspace) = await CreateProjectWithPageAsync("Reorder_Project", 1);
        Page page = pageWorkspace.Page;

        var geom = new RectangleTextRegionGeometry(10, 10, 50, 50);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var r1 = new TextRegion(Guid.NewGuid(), page.Id, geom, 1, TextRegionRole.Unknown, TextContainerType.Unknown, now, now, TextRegionOrigin.Manual);
        var r2 = new TextRegion(Guid.NewGuid(), page.Id, geom, 2, TextRegionRole.Unknown, TextContainerType.Unknown, now, now, TextRegionOrigin.Manual);
        var r3 = new TextRegion(Guid.NewGuid(), page.Id, geom, 3, TextRegionRole.Unknown, TextContainerType.Unknown, now, now, TextRegionOrigin.Manual);

        await _regionStore.AddAsync(workspace, r1, TestContext.Current.CancellationToken);
        await _regionStore.AddAsync(workspace, r2, TestContext.Current.CancellationToken);
        await _regionStore.AddAsync(workspace, r3, TestContext.Current.CancellationToken);

        // Reverse order: r3 (1), r2 (2), r1 (3)
        DateTimeOffset swapTime = now.AddMinutes(1);
        await _regionStore.ReorderPageRegionsAsync(
            workspace, page.Id, [r3.Id, r2.Id, r1.Id], swapTime, TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> reloaded = await _regionStore.GetForPageAsync(workspace, page.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, reloaded.Count);
        Assert.Equal(r3.Id, reloaded[0].Id);
        Assert.Equal(1, reloaded[0].ReadingOrder);
        Assert.Equal(r2.Id, reloaded[1].Id);
        Assert.Equal(2, reloaded[1].ReadingOrder);
        Assert.Equal(r1.Id, reloaded[2].Id);
        Assert.Equal(3, reloaded[2].ReadingOrder);

        // Verify userModifiedAt was updated
        Assert.Equal(swapTime, reloaded[0].UserModifiedAt);
    }

    private async Task<(ProjectWorkspace Workspace, PageWorkspace Page)> CreateProjectWithPageAsync(
        string projectName,
        int chapterNumber)
    {
        string sourceRoot = _temporaryWorkspace.GetPath(projectName);
        Directory.CreateDirectory(sourceRoot);
        string chapterFolder = Path.Combine(sourceRoot, "001");
        Directory.CreateDirectory(chapterFolder);
        string imagePath = Path.Combine(chapterFolder, "001.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes, TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);
        ChapterSource source = await new LocalFolderChapterSourceProvider().LoadAsync(
            chapterFolder, workspace.SourceRoot, TestContext.Current.CancellationToken);
        ChapterImportResult imported = await new ChapterService(new FileSystemChapterStore(), TimeProvider.System)
            .ImportAsync(
                workspace,
                new ImportChapterRequest(new ChapterNumber(chapterNumber), null, source),
                cancellationToken: TestContext.Current.CancellationToken);
        PageWorkspace page = Assert.Single(imported.Pages);

        return (workspace, page);
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
