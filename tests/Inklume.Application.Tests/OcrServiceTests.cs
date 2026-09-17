using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class OcrServiceTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly string _tempDirectory;

    public OcrServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "inklume-ocr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    [Fact]
    public async Task AnalyzePageAsync_ShouldCreateOcrRegionsWithUnknownRoleAndContainer()
    {
        (OcrService service, FakeOcrProvider provider, InMemoryOcrStore store, ProjectWorkspace workspace, Page page) = CreateFixture();

        var polygon = new PolygonTextRegionGeometry(
            [new ImagePoint(10, 10), new ImagePoint(100, 10), new ImagePoint(100, 50), new ImagePoint(10, 50)]);

        provider.PageResult = new OcrPageResult(
            [new OcrDetectedRegion(polygon, "안녕하세요", 0.95, 0.98)],
            new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        IReadOnlyList<TextRegion> results = await service.AnalyzePageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        TextRegion region = Assert.Single(results);
        Assert.Equal(TextRegionOrigin.OcrDetection, region.Origin);
        Assert.Equal(TextRegionRole.Unknown, region.Role);
        Assert.Equal(TextContainerType.Unknown, region.ContainerType);
        Assert.Equal(1, region.ReadingOrder);

        OcrRecognition recognition = Assert.Single(store.Recognitions.Values);
        Assert.Equal("안녕하세요", recognition.Text);
        Assert.Equal(0.95, recognition.RecognitionConfidence);
        Assert.Equal(0.98, recognition.DetectionConfidence);
        Assert.Equal("PaddleOCR", recognition.EngineName);
    }

    [Fact]
    public async Task AnalyzePageAsync_ShouldPreserveManualRegionsAndReplaceOldAutoRegions()
    {
        (OcrService service, FakeOcrProvider provider, InMemoryOcrStore store, ProjectWorkspace workspace, Page page) = CreateFixture();

        // 1. Existing manual region
        var manualRegion = new TextRegion(
            Guid.NewGuid(), page.Id,
            new RectangleTextRegionGeometry(50, 50, 60, 40), 1,
            TextRegionRole.Dialogue, TextContainerType.SpeechBubble,
            Timestamp, Timestamp, TextRegionOrigin.Manual);
        store.Regions.Add(manualRegion);

        // 2. Existing previous auto region
        var previousAutoRegion = new TextRegion(
            Guid.NewGuid(), page.Id,
            new RectangleTextRegionGeometry(10, 10, 20, 20), 2,
            TextRegionRole.Unknown, TextContainerType.Unknown,
            Timestamp, Timestamp, TextRegionOrigin.OcrDetection);
        store.Regions.Add(previousAutoRegion);

        // 3. New OCR run returning 1 detection
        var polygon = new PolygonTextRegionGeometry(
            [new ImagePoint(200, 200), new ImagePoint(300, 200), new ImagePoint(300, 250), new ImagePoint(200, 250)]);
        provider.PageResult = new OcrPageResult(
            [new OcrDetectedRegion(polygon, "새로운 텍스트", 0.92, 0.94)],
            new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        IReadOnlyList<TextRegion> currentRegions = await service.AnalyzePageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        // Manual region must remain
        Assert.Contains(store.Regions, r => r.Id == manualRegion.Id && r.Origin == TextRegionOrigin.Manual);
        // Previous auto region must be deleted
        Assert.DoesNotContain(store.Regions, r => r.Id == previousAutoRegion.Id);
        // New auto region must exist
        TextRegion newAuto = Assert.Single(currentRegions);
        Assert.Equal(TextRegionOrigin.OcrDetection, newAuto.Origin);
        Assert.Equal(manualRegion.ReadingOrder + 1, newAuto.ReadingOrder);
    }

    [Fact]
    public async Task AnalyzePageAsync_ShouldRejectOutOfBoundsGeometry()
    {
        (OcrService service, FakeOcrProvider provider, _, ProjectWorkspace workspace, Page page) = CreateFixture();

        // Coordinates out of RAW page bounds (pixelWidth: 1080, pixelHeight: 15000)
        var outOfBounds = new PolygonTextRegionGeometry(
            [new ImagePoint(1000, 10), new ImagePoint(2000, 10), new ImagePoint(2000, 50), new ImagePoint(1000, 50)]);

        provider.PageResult = new OcrPageResult(
            [new OcrDetectedRegion(outOfBounds, "OutOfBounds", 0.9, 0.9)],
            new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        ProjectOperationException ex = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.AnalyzePageAsync(workspace, page.Id, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.OcrInvalidResult, ex.Code);
    }

    [Fact]
    public async Task AnalyzePageAsync_ShouldPreservePreviousDataOnProviderFailure()
    {
        (OcrService service, FakeOcrProvider provider, InMemoryOcrStore store, ProjectWorkspace workspace, Page page) = CreateFixture();

        var autoRegion = new TextRegion(
            Guid.NewGuid(), page.Id,
            new RectangleTextRegionGeometry(10, 10, 20, 20), 1,
            TextRegionRole.Unknown, TextContainerType.Unknown,
            Timestamp, Timestamp, TextRegionOrigin.OcrDetection);
        store.Regions.Add(autoRegion);

        provider.ExceptionToThrow = new InvalidOperationException("Worker crashed");

        await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.AnalyzePageAsync(workspace, page.Id, TestContext.Current.CancellationToken));

        Assert.Single(store.Regions);
        Assert.Equal(autoRegion.Id, store.Regions[0].Id);
    }

    [Fact]
    public async Task RecognizeRegionAsync_ShouldUpdateOcrWithoutModifyingRegionGeometryOrOrigin()
    {
        (OcrService service, FakeOcrProvider provider, InMemoryOcrStore store, ProjectWorkspace workspace, Page page) = CreateFixture();

        var manualRegion = new TextRegion(
            Guid.NewGuid(), page.Id,
            new RectangleTextRegionGeometry(50, 50, 100, 100), 1,
            TextRegionRole.Thought, TextContainerType.ThoughtBubble,
            Timestamp, Timestamp, TextRegionOrigin.Manual);
        store.Regions.Add(manualRegion);

        provider.RegionResult = new OcrRegionResult(
            "수동 영역 텍스트", 0.91,
            new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        OcrRecognition recognition = await service.RecognizeRegionAsync(
            workspace, page.Id, manualRegion.Id, TestContext.Current.CancellationToken);

        Assert.Equal(manualRegion.Id, recognition.TextRegionId);
        Assert.Equal("수동 영역 텍스트", recognition.Text);
        Assert.Equal(0.91, recognition.RecognitionConfidence);

        // Verify region itself is untouched
        TextRegion storedRegion = Assert.Single(store.Regions);
        Assert.Equal(TextRegionOrigin.Manual, storedRegion.Origin);
        Assert.Equal(TextRegionRole.Thought, storedRegion.Role);
        Assert.Equal(TextContainerType.ThoughtBubble, storedRegion.ContainerType);
        Assert.Equal(manualRegion.Geometry, storedRegion.Geometry);
    }

    private (OcrService, FakeOcrProvider, InMemoryOcrStore, ProjectWorkspace, Page) CreateFixture()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Project", "Series", Timestamp, Timestamp);
        string sourceRoot = Path.Combine(_tempDirectory, "source");
        string dataRoot = Path.Combine(_tempDirectory, "data");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(dataRoot);

        string imageRelativePath = "page1.png";
        string fullImagePath = Path.Combine(sourceRoot, imageRelativePath);
        File.WriteAllBytes(fullImagePath, [1, 2, 3]);

        var workspace = new ProjectWorkspace(project, sourceRoot, dataRoot);
        var page = new Page(
            Guid.NewGuid(), Guid.NewGuid(), 1, imageRelativePath, imageRelativePath,
            new string('A', 64), 1080, 15000);

        var store = new InMemoryOcrStore();
        store.Pages[page.Id] = page;

        var provider = new FakeOcrProvider();
        var service = new OcrService(provider, store, store);

        return (service, provider, store, workspace, page);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private sealed class FakeOcrProvider : IOcrProvider
    {
        public OcrPageResult PageResult { get; set; } = new(
            [], new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        public OcrRegionResult RegionResult { get; set; } = new(
            "Fake", 0.99, new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"));

        public Exception? ExceptionToThrow { get; set; }

        public Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(PageResult);
        }

        public Task<OcrRegionResult> RecognizeRegionAsync(
            string imagePath, TextRegionGeometry geometry, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(RegionResult);
        }
    }

    private sealed class InMemoryOcrStore : IOcrStore, ITextRegionStore
    {
        public Dictionary<Guid, Page> Pages { get; } = [];
        public List<TextRegion> Regions { get; } = [];
        public Dictionary<Guid, OcrRecognition> Recognitions { get; } = [];

        public Task<Page?> GetPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Pages.GetValueOrDefault(pageId));

        public Task<IReadOnlyList<TextRegion>> GetForPageAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TextRegion>>(
                [.. Regions.Where(r => r.PageId == pageId).OrderBy(r => r.ReadingOrder)]);

        public Task<int> GetNextReadingOrderAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Regions.Where(r => r.PageId == pageId).Select(r => r.ReadingOrder).DefaultIfEmpty().Max() + 1);

        public Task AddAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
        {
            Regions.Add(region);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
        {
            int index = Regions.FindIndex(r => r.Id == region.Id);
            if (index >= 0) Regions[index] = region;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
        {
            Regions.RemoveAll(r => r.Id == regionId);
            Recognitions.Remove(regionId);
            return Task.CompletedTask;
        }

        public Task ReorderPageRegionsAsync(
            ProjectWorkspace workspace,
            Guid pageId,
            IReadOnlyList<Guid> regionIdsInOrder,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < regionIdsInOrder.Count; i++)
            {
                Guid id = regionIdsInOrder[i];
                int index = Regions.FindIndex(r => r.Id == id && r.PageId == pageId);
                if (index >= 0)
                {
                    Regions[index] = Regions[index].WithReadingOrder(i + 1, updatedAt);
                }
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
        {
            var pageRegionIds = Regions.Where(r => r.PageId == pageId).Select(r => r.Id).ToHashSet();
            return Task.FromResult<IReadOnlyDictionary<Guid, OcrRecognition>>(
                Recognitions.Where(kv => pageRegionIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        public Task<OcrRecognition?> GetRecognitionForRegionAsync(
            ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
            => Task.FromResult(Recognitions.GetValueOrDefault(regionId));

        public Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(
            ProjectWorkspace workspace,
            Guid pageId,
            IReadOnlyList<OcrDetectedRegion> detectedRegions,
            OcrEngineMetadata engineMetadata,
            CancellationToken cancellationToken)
        {
            // Delete only OCR detections on this page
            var toDelete = Regions.Where(r => r.PageId == pageId && r.Origin == TextRegionOrigin.OcrDetection).ToList();
            foreach (var del in toDelete)
            {
                Regions.Remove(del);
                Recognitions.Remove(del.Id);
            }

            // Keep manual reading orders
            int maxManualOrder = Regions.Where(r => r.PageId == pageId)
                .Select(r => r.ReadingOrder).DefaultIfEmpty(0).Max();

            var newRegions = new List<TextRegion>();
            int order = maxManualOrder + 1;
            foreach (var detected in detectedRegions)
            {
                var id = Guid.NewGuid();
                var region = new TextRegion(
                    id, pageId, detected.Geometry, order++,
                    TextRegionRole.Unknown, TextContainerType.Unknown,
                    Timestamp, Timestamp, TextRegionOrigin.OcrDetection);
                Regions.Add(region);
                newRegions.Add(region);

                Recognitions[id] = new OcrRecognition(
                    Guid.NewGuid(), id, detected.Text, detected.RecognitionConfidence, detected.DetectionConfidence,
                    engineMetadata.EngineName, engineMetadata.EngineVersion,
                    engineMetadata.DetectionModel, engineMetadata.RecognitionModel, engineMetadata.ModelProfile,
                    Timestamp, Timestamp);
            }

            return Task.FromResult<IReadOnlyList<TextRegion>>(newRegions);
        }

        public Task<OcrRecognition> SaveRegionRecognitionAsync(
            ProjectWorkspace workspace,
            Guid regionId,
            OcrRegionResult regionResult,
            CancellationToken cancellationToken)
        {
            var recognition = new OcrRecognition(
                Guid.NewGuid(), regionId, regionResult.Text, regionResult.RecognitionConfidence, null,
                regionResult.EngineInfo.EngineName, regionResult.EngineInfo.EngineVersion,
                regionResult.EngineInfo.DetectionModel, regionResult.EngineInfo.RecognitionModel, regionResult.EngineInfo.ModelProfile,
                Timestamp, Timestamp);
            Recognitions[regionId] = recognition;
            return Task.FromResult(recognition);
        }
    }
}
