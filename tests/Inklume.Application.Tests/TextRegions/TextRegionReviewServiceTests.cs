using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Application.Tests.TextRegions;

public sealed class TextRegionReviewServiceTests : IDisposable
{
    private static readonly DateTimeOffset InitialTime = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterTime = new(2026, 9, 17, 11, 0, 0, TimeSpan.Zero);
    private readonly string _tempDirectory;

    public TextRegionReviewServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "inklume-review-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task SaveReviewedTextAsync_ShouldPreserveRawOcrText_AndSetEffectiveText()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        await ocrStore.SaveRegionRecognitionAsync(
            workspace,
            region.Id,
            new OcrRegionResult("안녕하세오", 0.92, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);

        TextRegion updated = await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, region.Id, "안녕하세요", TestContext.Current.CancellationToken);

        Assert.Equal("안녕하세요", updated.ReviewedText);
        Assert.NotNull(updated.UserModifiedAt);

        // OCR recognition must be unchanged
        OcrRecognition? ocr = await ocrStore.GetRecognitionForRegionAsync(
            workspace, region.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(ocr);
        Assert.Equal("안녕하세오", ocr.Text);

        RegionSourceTextDto dto = await reviewService.GetEffectiveSourceTextAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);
        Assert.Equal("안녕하세요", dto.EffectiveSourceText);
        Assert.Equal("안녕하세오", dto.RawOcrText);
        Assert.Equal("안녕하세요", dto.ReviewedText);
    }

    [Fact]
    public async Task EffectiveSourceText_ShouldFallBackToRawOcr_WhenReviewedTextIsNull()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        await ocrStore.SaveRegionRecognitionAsync(
            workspace,
            region.Id,
            new OcrRegionResult("안녕하세오", 0.90, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);

        RegionSourceTextDto dto = await reviewService.GetEffectiveSourceTextAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);

        Assert.Null(dto.ReviewedText);
        Assert.Equal("안녕하세오", dto.RawOcrText);
        Assert.Equal("안녕하세오", dto.EffectiveSourceText);
    }

    [Fact]
    public async Task ManualRegion_CanHaveReviewedText_WithoutOcr()
    {
        var (reviewService, _, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual);

        TextRegion updated = await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, region.Id, "Manual Text Entry", TestContext.Current.CancellationToken);

        Assert.Equal("Manual Text Entry", updated.ReviewedText);

        RegionSourceTextDto dto = await reviewService.GetEffectiveSourceTextAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);
        Assert.Null(dto.RawOcrText);
        Assert.Equal("Manual Text Entry", dto.EffectiveSourceText);
    }

    [Fact]
    public async Task ResetReviewedTextAsync_ShouldReturnEffectiveTextToRawOcr_AndPreserveOcr()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        await ocrStore.SaveRegionRecognitionAsync(
            workspace,
            region.Id,
            new OcrRegionResult("OCR Text", 0.95, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);

        await reviewService.SaveReviewedTextAsync(workspace, page.Id, region.Id, "Review Override", TestContext.Current.CancellationToken);
        TextRegion reset = await reviewService.ResetReviewedTextAsync(workspace, page.Id, region.Id, TestContext.Current.CancellationToken);

        Assert.Null(reset.ReviewedText);

        RegionSourceTextDto dto = await reviewService.GetEffectiveSourceTextAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);
        Assert.Equal("OCR Text", dto.EffectiveSourceText);
        Assert.Null(dto.ReviewedText);

        OcrRecognition? ocr = await ocrStore.GetRecognitionForRegionAsync(
            workspace, region.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(ocr);
        Assert.Equal("OCR Text", ocr.Text);
    }

    [Fact]
    public async Task EmptyReviewedText_ShouldOverrideOcrWithEmptyString()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        await ocrStore.SaveRegionRecognitionAsync(
            workspace,
            region.Id,
            new OcrRegionResult("OCR Text", 0.95, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);

        TextRegion updated = await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, region.Id, string.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, updated.ReviewedText);

        RegionSourceTextDto dto = await reviewService.GetEffectiveSourceTextAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, dto.EffectiveSourceText);
        Assert.NotNull(dto.EffectiveSourceText);
        Assert.True(dto.HasReviewedOverride);
    }

    [Fact]
    public async Task MarkReviewed_And_MarkPending_ShouldTransitionStateSafely()
    {
        var (reviewService, _, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);

        // Mark Reviewed
        TextRegion reviewed = await reviewService.SetReviewStatusAsync(
            workspace, page.Id, region.Id, TextRegionReviewStatus.Reviewed, TestContext.Current.CancellationToken);
        Assert.Equal(TextRegionReviewStatus.Reviewed, reviewed.ReviewStatus);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.NotNull(reviewed.UserModifiedAt);

        // Mark Pending
        TextRegion pending = await reviewService.SetReviewStatusAsync(
            workspace, page.Id, region.Id, TextRegionReviewStatus.Pending, TestContext.Current.CancellationToken);
        Assert.Equal(TextRegionReviewStatus.Pending, pending.ReviewStatus);
        Assert.Null(pending.ReviewedAt);
        Assert.NotNull(pending.UserModifiedAt);
    }

    [Fact]
    public async Task UpdateClassificationAsync_ShouldUpdateRoleContainerAndUserModifiedAt()
    {
        var (reviewService, _, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        Assert.Null(region.UserModifiedAt);

        TextRegion updated = await reviewService.UpdateClassificationAsync(
            workspace, page.Id, region.Id, TextRegionRole.Dialogue, TextContainerType.SpeechBubble, TestContext.Current.CancellationToken);

        Assert.Equal(TextRegionRole.Dialogue, updated.Role);
        Assert.Equal(TextContainerType.SpeechBubble, updated.ContainerType);
        Assert.NotNull(updated.UserModifiedAt);
    }

    [Fact]
    public async Task ReadingOrder_MoveUpAndMoveDown_ShouldSwapOrdersDeterministicallyAndUniquely()
    {
        var (reviewService, _, regionStore, workspace, page) = CreateFixture();
        TextRegion r1 = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual);
        TextRegion r2 = await CreateRegionAsync(regionStore, workspace, page.Id, 2, TextRegionOrigin.Manual);
        TextRegion r3 = await CreateRegionAsync(regionStore, workspace, page.Id, 3, TextRegionOrigin.Manual);

        // Move r3 up -> should swap r2 and r3
        IReadOnlyList<TextRegion> reordered = await reviewService.MoveRegionUpAsync(
            workspace, page.Id, r3.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, reordered.Count);
        Assert.Equal(r1.Id, reordered[0].Id);
        Assert.Equal(1, reordered[0].ReadingOrder);
        Assert.Equal(r3.Id, reordered[1].Id);
        Assert.Equal(2, reordered[1].ReadingOrder);
        Assert.Equal(r2.Id, reordered[2].Id);
        Assert.Equal(3, reordered[2].ReadingOrder);

        // Move r1 down -> should swap r1 and r3
        IReadOnlyList<TextRegion> reorderedDown = await reviewService.MoveRegionDownAsync(
            workspace, page.Id, r1.Id, TestContext.Current.CancellationToken);

        Assert.Equal(r3.Id, reorderedDown[0].Id);
        Assert.Equal(1, reorderedDown[0].ReadingOrder);
        Assert.Equal(r1.Id, reorderedDown[1].Id);
        Assert.Equal(2, reorderedDown[1].ReadingOrder);
        Assert.Equal(r2.Id, reorderedDown[2].Id);
        Assert.Equal(3, reorderedDown[2].ReadingOrder);

        // Reading orders are contiguous and unique 1, 2, 3
        Assert.Equal([1, 2, 3], reorderedDown.Select(r => r.ReadingOrder));
    }

    [Fact]
    public async Task GetPageReviewProgressAsync_ShouldCalculateProgressCorrectly()
    {
        var (reviewService, _, regionStore, workspace, page) = CreateFixture();
        TextRegion r1 = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual);
        TextRegion r2 = await CreateRegionAsync(regionStore, workspace, page.Id, 2, TextRegionOrigin.Manual);

        var (reviewed0, total0) = await reviewService.GetPageReviewProgressAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, total0);
        Assert.Equal(0, reviewed0);

        await reviewService.SetReviewStatusAsync(
            workspace, page.Id, r1.Id, TextRegionReviewStatus.Reviewed, TestContext.Current.CancellationToken);

        var (reviewed1, total1) = await reviewService.GetPageReviewProgressAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, total1);
        Assert.Equal(1, reviewed1);
    }

    [Fact]
    public async Task RerunRegionOcr_ShouldUpdateOcrRecognition_AndPreserveReviewedTextAndMetadata()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        var ocrProvider = new FakeOcrProvider
        {
            RegionResult = new OcrRegionResult("OCR Rerun Text", 0.99, CreateEngineMetadata())
        };
        var ocrService = new OcrService(ocrProvider, ocrStore, regionStore);

        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.OcrDetection);
        await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, region.Id, "Human Correction", TestContext.Current.CancellationToken);
        await reviewService.UpdateClassificationAsync(
            workspace, page.Id, region.Id, TextRegionRole.Dialogue, TextContainerType.SpeechBubble, TestContext.Current.CancellationToken);

        // Rerun region OCR
        OcrRecognition updatedOcr = await ocrService.RecognizeRegionAsync(
            workspace, page.Id, region.Id, TestContext.Current.CancellationToken);
        Assert.Equal("OCR Rerun Text", updatedOcr.Text);

        // Verify region still has reviewed text and classification
        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);
        TextRegion reloaded = Assert.Single(regions);
        Assert.Equal("Human Correction", reloaded.ReviewedText);
        Assert.Equal(TextRegionRole.Dialogue, reloaded.Role);
        Assert.Equal(TextContainerType.SpeechBubble, reloaded.ContainerType);
        Assert.Equal(1, reloaded.ReadingOrder);
    }

    [Fact]
    public async Task PageOcrRerun_ShouldPreserveManualAndProtectedRegions_AndSuppressOverlaps()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();

        // 1. Manual region
        TextRegion manual = await CreateRegionAsync(
            regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual, new RectangleTextRegionGeometry(10, 10, 50, 50));

        // 2. Untouched OCR detection
        TextRegion untouchedAuto = await CreateRegionAsync(
            regionStore, workspace, page.Id, 2, TextRegionOrigin.OcrDetection, new RectangleTextRegionGeometry(70, 10, 50, 50));

        // 3. User-modified OCR detection (reviewed text)
        TextRegion reviewedAuto = await CreateRegionAsync(
            regionStore, workspace, page.Id, 3, TextRegionOrigin.OcrDetection, new RectangleTextRegionGeometry(10, 70, 50, 50));
        await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, reviewedAuto.Id, "Reviewed Override", TestContext.Current.CancellationToken);

        // 4. User-modified OCR detection (role classified)
        TextRegion classifiedAuto = await CreateRegionAsync(
            regionStore, workspace, page.Id, 4, TextRegionOrigin.OcrDetection, new RectangleTextRegionGeometry(70, 70, 50, 50));
        await reviewService.UpdateClassificationAsync(
            workspace, page.Id, classifiedAuto.Id, TextRegionRole.Dialogue, TextContainerType.SpeechBubble, TestContext.Current.CancellationToken);

        // New provider result:
        // - overlaps reviewedAuto with IoU >= 0.50 -> must be SUPPRESSED
        // - new detection in fresh area -> must be ADDED
        var polyOverlapping = new PolygonTextRegionGeometry(
            [new ImagePoint(10, 70), new ImagePoint(60, 70), new ImagePoint(60, 120), new ImagePoint(10, 120)]);
        var polyNew = new PolygonTextRegionGeometry(
            [new ImagePoint(10, 200), new ImagePoint(60, 200), new ImagePoint(60, 250), new ImagePoint(10, 250)]);

        var provider = new FakeOcrProvider
        {
            PageResult = new OcrPageResult(
            [
                new OcrDetectedRegion(polyOverlapping, "Overlapping Text", 0.95, 0.95),
                new OcrDetectedRegion(polyNew, "Fresh New Detection", 0.95, 0.95)
            ],
            CreateEngineMetadata())
        };

        var ocrService = new OcrService(provider, ocrStore, regionStore);
        await ocrService.AnalyzePageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> results = await regionStore.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        // Untouched auto was replaced. Manual, reviewedAuto, and classifiedAuto were preserved.
        // Overlapping detection was suppressed. Fresh new detection was added.
        Assert.Contains(results, r => r.Id == manual.Id);
        Assert.Contains(results, r => r.Id == reviewedAuto.Id);
        Assert.Contains(results, r => r.Id == classifiedAuto.Id);
        Assert.DoesNotContain(results, r => r.Id == untouchedAuto.Id);

        // Verify fresh new detection was added with unique reading order
        TextRegion freshRegion = Assert.Single(results, r => r.Id != manual.Id && r.Id != reviewedAuto.Id && r.Id != classifiedAuto.Id);
        Assert.Equal(TextRegionOrigin.OcrDetection, freshRegion.Origin);
        Assert.True(freshRegion.ReadingOrder > 0);

        // Ensure reading orders are distinct
        Assert.Equal(results.Count, results.Select(r => r.ReadingOrder).Distinct().Count());
    }

    [Fact]
    public async Task PageOcr_ProviderFailure_ShouldPreserveAllExistingRegions()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual);
        await reviewService.SaveReviewedTextAsync(
            workspace, page.Id, region.Id, "Keep Me", TestContext.Current.CancellationToken);

        var provider = new FakeOcrProvider { ThrowOnAnalyze = true };
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        await Assert.ThrowsAsync<ProjectOperationException>(() =>
            ocrService.AnalyzePageAsync(workspace, page.Id, TestContext.Current.CancellationToken));

        IReadOnlyList<TextRegion> reloaded = await regionStore.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);
        TextRegion remaining = Assert.Single(reloaded);
        Assert.Equal(region.Id, remaining.Id);
        Assert.Equal("Keep Me", remaining.ReviewedText);
    }

    [Fact]
    public async Task PageOcr_Cancellation_ShouldPreserveAllExistingRegions()
    {
        var (reviewService, ocrStore, regionStore, workspace, page) = CreateFixture();
        TextRegion region = await CreateRegionAsync(regionStore, workspace, page.Id, 1, TextRegionOrigin.Manual);

        var provider = new FakeOcrProvider();
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ocrService.AnalyzePageAsync(workspace, page.Id, cts.Token));

        IReadOnlyList<TextRegion> reloaded = await regionStore.GetForPageAsync(
            workspace, page.Id, CancellationToken.None);
        Assert.Single(reloaded);
    }

    private (TextRegionReviewService ReviewService, InMemoryOcrStore OcrStore, InMemoryTextRegionStore RegionStore, ProjectWorkspace Workspace, Page Page) CreateFixture()
    {
        var timeProvider = new FixedTimeProvider(InitialTime);
        var regionStore = new InMemoryTextRegionStore();
        var ocrStore = new InMemoryOcrStore(timeProvider, regionStore);

        string sourceRoot = Path.Combine(_tempDirectory, "source");
        string dataRoot = Path.Combine(_tempDirectory, "data");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(dataRoot);

        var workspace = new ProjectWorkspace(
            new TranslationProject(Guid.NewGuid(), "Test Manga", "Series", InitialTime, InitialTime),
            sourceRoot,
            dataRoot);

        string imageRelativePath = "01.png";
        string fullImagePath = Path.Combine(sourceRoot, imageRelativePath);
        File.WriteAllBytes(fullImagePath, [1, 2, 3]);

        var page = new Page(Guid.NewGuid(), Guid.NewGuid(), 1, imageRelativePath, imageRelativePath, new string('0', 64), 1000, 1500);

        regionStore.Pages[page.Id] = page;
        ocrStore.Pages[page.Id] = page;

        var reviewService = new TextRegionReviewService(regionStore, ocrStore, timeProvider);
        return (reviewService, ocrStore, regionStore, workspace, page);
    }

    private static async Task<TextRegion> CreateRegionAsync(
        InMemoryTextRegionStore store,
        ProjectWorkspace workspace,
        Guid pageId,
        int readingOrder,
        TextRegionOrigin origin,
        TextRegionGeometry? geometry = null)
    {
        var geom = geometry ?? new RectangleTextRegionGeometry(10, 10, 50, 50);
        var region = new TextRegion(
            Guid.NewGuid(),
            pageId,
            geom,
            readingOrder,
            TextRegionRole.Unknown,
            TextContainerType.Unknown,
            InitialTime,
            InitialTime,
            origin);

        await store.AddAsync(workspace, region, CancellationToken.None);
        return region;
    }

    private static OcrEngineMetadata CreateEngineMetadata()
        => new("PaddleOCR", "3.7.0", "det", "rec", "Korean + English");

    private sealed class FixedTimeProvider(DateTimeOffset time) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => time;
    }

    private sealed class FakeOcrProvider : IOcrProvider
    {
        public bool ThrowOnAnalyze { get; set; }
        public OcrPageResult? PageResult { get; set; }
        public OcrRegionResult? RegionResult { get; set; }

        public Task<OcrPageResult> AnalyzePageAsync(
            string imagePath,
            CancellationToken cancellationToken)
        {
            if (ThrowOnAnalyze) throw new InvalidOperationException("Provider failed.");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PageResult ?? new OcrPageResult([], CreateEngineMetadata()));
        }

        public Task<OcrRegionResult> RecognizeRegionAsync(
            string imagePath,
            TextRegionGeometry geometry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RegionResult ?? new OcrRegionResult("Default", 0.9, CreateEngineMetadata()));
        }
    }

    private sealed class InMemoryTextRegionStore : ITextRegionStore
    {
        public Dictionary<Guid, Page> Pages { get; } = [];
        public List<TextRegion> Regions { get; } = [];

        public Task<Page?> GetPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Pages.GetValueOrDefault(pageId));

        public Task<IReadOnlyList<TextRegion>> GetForPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TextRegion>>([.. Regions.Where(r => r.PageId == pageId).OrderBy(r => r.ReadingOrder)]);

        public Task<int> GetNextReadingOrderAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Regions.Where(r => r.PageId == pageId).Select(r => r.ReadingOrder).DefaultIfEmpty(0).Max() + 1);

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
                    Regions[index] = Regions[index].WithReadingOrder(i + 1, updatedAt, updatedAt);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryOcrStore(TimeProvider timeProvider, InMemoryTextRegionStore regionStore) : IOcrStore
    {
        public Dictionary<Guid, Page> Pages { get; } = [];
        public Dictionary<Guid, OcrRecognition> Recognitions { get; } = [];

        public Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, OcrRecognition>>(Recognitions);

        public Task<OcrRecognition?> GetRecognitionForRegionAsync(
            ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
            => Task.FromResult(Recognitions.GetValueOrDefault(regionId));

        public Task<OcrRecognition> SaveRegionRecognitionAsync(
            ProjectWorkspace workspace,
            Guid regionId,
            OcrRegionResult result,
            CancellationToken cancellationToken)
        {
            var recognition = new OcrRecognition(
                Guid.NewGuid(),
                regionId,
                result.Text,
                result.RecognitionConfidence,
                null,
                result.EngineInfo.EngineName,
                result.EngineInfo.EngineVersion,
                result.EngineInfo.DetectionModel,
                result.EngineInfo.RecognitionModel,
                result.EngineInfo.ModelProfile,
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow());
            Recognitions[regionId] = recognition;
            return Task.FromResult(recognition);
        }

        public Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(
            ProjectWorkspace workspace,
            Guid pageId,
            IReadOnlyList<OcrDetectedRegion> detectedRegions,
            OcrEngineMetadata engineMetadata,
            CancellationToken cancellationToken)
        {
            var pageRegions = regionStore.Regions.Where(r => r.PageId == pageId).ToList();
            var protectedRegions = pageRegions.Where(r =>
                r.Origin == TextRegionOrigin.Manual ||
                r.UserModifiedAt != null ||
                r.ReviewStatus == TextRegionReviewStatus.Reviewed ||
                r.ReviewedText != null ||
                r.Role != TextRegionRole.Unknown ||
                r.ContainerType != TextContainerType.Unknown).ToList();

            var autoToDelete = pageRegions.Where(r => !protectedRegions.Contains(r)).ToList();
            foreach (var r in autoToDelete)
            {
                regionStore.Regions.Remove(r);
                Recognitions.Remove(r.Id);
            }

            var protectedBounds = protectedRegions.Select(r => r.Geometry.Bounds).ToList();
            int nextOrder = protectedRegions.Select(r => r.ReadingOrder).DefaultIfEmpty(0).Max() + 1;
            var created = new List<TextRegion>();

            foreach (var det in detectedRegions)
            {
                if (OcrOverlapPolicy.ShouldSuppressDetection(det.Geometry.Bounds, protectedBounds))
                {
                    continue;
                }

                var newRegion = new TextRegion(
                    Guid.NewGuid(),
                    pageId,
                    det.Geometry,
                    nextOrder++,
                    TextRegionRole.Unknown,
                    TextContainerType.Unknown,
                    timeProvider.GetUtcNow(),
                    timeProvider.GetUtcNow(),
                    TextRegionOrigin.OcrDetection);

                regionStore.Regions.Add(newRegion);
                Recognitions[newRegion.Id] = new OcrRecognition(
                    Guid.NewGuid(),
                    newRegion.Id,
                    det.Text,
                    det.RecognitionConfidence,
                    det.DetectionConfidence,
                    engineMetadata.EngineName,
                    engineMetadata.EngineVersion,
                    engineMetadata.DetectionModel,
                    engineMetadata.RecognitionModel,
                    engineMetadata.ModelProfile,
                    timeProvider.GetUtcNow(),
                    timeProvider.GetUtcNow());

                created.Add(newRegion);
            }

            return Task.FromResult<IReadOnlyList<TextRegion>>(created);
        }
    }
}
