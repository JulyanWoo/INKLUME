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

public sealed class OcrPersistenceTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly FileSystemProjectStore _store;
    private readonly SqliteTextRegionStore _regionStore = new();
    private readonly SqliteOcrStore _ocrStore = new();

    public OcrPersistenceTests()
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
    public async Task OcrPersistence_ShouldCascadeDeleteWhenTextRegionDeleted()
    {
        (ProjectWorkspace workspace, PageWorkspace pageWorkspace) = await CreateProjectWithPageAsync("Cascade Test", 1);
        Page page = pageWorkspace.Page;

        var polygon = new PolygonTextRegionGeometry(
            [new ImagePoint(10, 10), new ImagePoint(50, 10), new ImagePoint(50, 40), new ImagePoint(10, 40)]);

        var engineMetadata = new OcrEngineMetadata(
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English");

        IReadOnlyList<TextRegion> created = await _ocrStore.ReplacePageOcrRegionsAsync(
            workspace, page.Id,
            [new OcrDetectedRegion(polygon, "대사 텍스트", 0.95, 0.92)],
            engineMetadata,
            TestContext.Current.CancellationToken);

        TextRegion autoRegion = Assert.Single(created);

        // Verify recognition exists
        OcrRecognition? recognition = await _ocrStore.GetRecognitionForRegionAsync(
            workspace, autoRegion.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(recognition);
        Assert.Equal("대사 텍스트", recognition.Text);

        // Delete text region
        await _regionStore.DeleteAsync(workspace, autoRegion.Id, TestContext.Current.CancellationToken);

        // Verify OCR recognition was cascade deleted
        OcrRecognition? deletedRecognition = await _ocrStore.GetRecognitionForRegionAsync(
            workspace, autoRegion.Id, TestContext.Current.CancellationToken);
        Assert.Null(deletedRecognition);
    }

    [Fact]
    public async Task ReplacePageOcrRegions_ShouldPreserveManualRegionsAndPreventDuplicates()
    {
        (ProjectWorkspace workspace, PageWorkspace pageWorkspace) = await CreateProjectWithPageAsync("Replace Test", 1);
        Page page = pageWorkspace.Page;

        // 1. Create a manual region
        var manualGeometry = new RectangleTextRegionGeometry(10, 10, 30, 30);
        var manualRegion = new TextRegion(
            Guid.NewGuid(), page.Id, manualGeometry, 1,
            TextRegionRole.Dialogue, TextContainerType.SpeechBubble,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TextRegionOrigin.Manual);
        await _regionStore.AddAsync(workspace, manualRegion, TestContext.Current.CancellationToken);

        // Attach OCR to manual region
        var engineMetadata = new OcrEngineMetadata(
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English");
        await _ocrStore.SaveRegionRecognitionAsync(
            workspace, manualRegion.Id,
            new OcrRegionResult("수동 인식", 0.88, engineMetadata),
            TestContext.Current.CancellationToken);

        // 2. Run Page OCR first time (2 auto regions)
        var poly1 = new PolygonTextRegionGeometry(
            [new ImagePoint(50, 50), new ImagePoint(100, 50), new ImagePoint(100, 90), new ImagePoint(50, 90)]);
        var poly2 = new PolygonTextRegionGeometry(
            [new ImagePoint(50, 100), new ImagePoint(100, 100), new ImagePoint(100, 140), new ImagePoint(50, 140)]);

        await _ocrStore.ReplacePageOcrRegionsAsync(
            workspace, page.Id,
            [
                new OcrDetectedRegion(poly1, "첫 번째", 0.91, 0.90),
                new OcrDetectedRegion(poly2, "두 번째", 0.92, 0.91)
            ],
            engineMetadata,
            TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> regionsAfterRun1 = await _regionStore.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);
        Assert.Equal(3, regionsAfterRun1.Count);

        // 3. Rerun Page OCR with 1 detection
        var poly3 = new PolygonTextRegionGeometry(
            [new ImagePoint(20, 200), new ImagePoint(80, 200), new ImagePoint(80, 250), new ImagePoint(20, 250)]);

        await _ocrStore.ReplacePageOcrRegionsAsync(
            workspace, page.Id,
            [new OcrDetectedRegion(poly3, "재실행 텍스트", 0.96, 0.95)],
            engineMetadata,
            TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> regionsAfterRun2 = await _regionStore.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        // Manual region + 1 new auto region = 2 regions
        Assert.Equal(2, regionsAfterRun2.Count);
        TextRegion manualFound = Assert.Single(regionsAfterRun2, r => r.Origin == TextRegionOrigin.Manual);
        Assert.Equal(manualRegion.Id, manualFound.Id);
        Assert.Equal(1, manualFound.ReadingOrder);

        // Manual OCR preserved
        OcrRecognition? manualOcr = await _ocrStore.GetRecognitionForRegionAsync(
            workspace, manualRegion.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(manualOcr);
        Assert.Equal("수동 인식", manualOcr.Text);

        // Auto region has order 2
        TextRegion autoFound = Assert.Single(regionsAfterRun2, r => r.Origin == TextRegionOrigin.OcrDetection);
        Assert.Equal(2, autoFound.ReadingOrder);

        OcrRecognition? autoOcr = await _ocrStore.GetRecognitionForRegionAsync(
            workspace, autoFound.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(autoOcr);
        Assert.Equal("재실행 텍스트", autoOcr.Text);
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
}
