using System.IO;
using System.Windows.Media;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class VisualEditorOcrTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string _tempDirectory;

    public VisualEditorOcrTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "inklume-editor-ocr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllBytes(Path.Combine(_tempDirectory, "1.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
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
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task RunPageOcrCommand_ShouldBeDisabled_WhenNoPageOrGenericImage()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var ocrStore = new InMemoryOcrStore();
        var provider = new FakeOcrProvider();
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        var viewModel = new VisualEditorViewModel(
            workspace,
            new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            new PreviewLoaderStub(),
            ocrService);

        Assert.False(viewModel.RunPageOcrCommand.CanExecute(null));

        await viewModel.LoadGenericImageAsync(@"C:\Project\raw.png", TestContext.Current.CancellationToken);
        Assert.False(viewModel.RunPageOcrCommand.CanExecute(null));

        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);
        Assert.True(viewModel.RunPageOcrCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunPageOcrCommand_ShouldExecute_AndUpdateRegionsAndRecognitions()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var ocrStore = new InMemoryOcrStore();
        var provider = new FakeOcrProvider
        {
            PageResultFunc = _ => new OcrPageResult(
            [
                new OcrDetectedRegion(
                    new PolygonTextRegionGeometry(new RectangleTextRegionGeometry(10, 20, 100, 50).Points),
                    "안녕하세요",
                    0.95,
                    0.92)
            ],
            new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"))
        };
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        var viewModel = new VisualEditorViewModel(
            workspace,
            new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            new PreviewLoaderStub(),
            ocrService);

        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);
        await viewModel.RunPageOcrCommand.ExecuteAsync(null);

        Assert.Single(viewModel.TextRegions);
        TextRegion region = viewModel.TextRegions[0];
        Assert.Equal(TextRegionOrigin.OcrDetection, region.Origin);

        viewModel.SelectRegion(region);
        Assert.True(viewModel.HasSelectedRegionOcr);
        Assert.Equal("안녕하세요", viewModel.SelectedRegionOcrText);
        Assert.Equal("OCR Detection", viewModel.SelectedRegionOriginDisplay);
        Assert.Contains("95", viewModel.SelectedRegionOcrRecognitionConfidenceDisplay);
        Assert.Contains("92", viewModel.SelectedRegionOcrDetectionConfidenceDisplay);
        Assert.Equal("PaddleOCR 3.7.0", viewModel.SelectedRegionOcrEngineDisplay);
        Assert.Equal("Korean + English", viewModel.SelectedRegionOcrProfileDisplay);
    }

    [Fact]
    public async Task RecognizeSelectedRegionCommand_ShouldBeEnabledOnlyWhenRegionSelected_AndUpdateRecognition()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var ocrStore = new InMemoryOcrStore();
        var provider = new FakeOcrProvider
        {
            RegionResultFunc = (_, _) => new OcrRegionResult(
                "Manual Recognized Text",
                0.98,
                new OcrEngineMetadata("PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English"))
        };
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        var viewModel = new VisualEditorViewModel(
            workspace,
            new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            new PreviewLoaderStub(),
            ocrService);

        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);
        Assert.False(viewModel.RecognizeSelectedRegionCommand.CanExecute(null));

        TextRegion manualRegion = await viewModel.CreateRectangleAsync(
            new ImagePoint(50, 50), new ImagePoint(200, 150), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        Assert.True(viewModel.RecognizeSelectedRegionCommand.CanExecute(null));
        Assert.False(viewModel.HasSelectedRegionOcr);
        Assert.Equal("Manual", viewModel.SelectedRegionOriginDisplay);

        await viewModel.RecognizeSelectedRegionCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSelectedRegionOcr);
        Assert.Equal("Manual Recognized Text", viewModel.SelectedRegionOcrText);
        Assert.Equal("Manual", viewModel.SelectedRegionOriginDisplay);
        Assert.Contains("98", viewModel.SelectedRegionOcrRecognitionConfidenceDisplay);
    }

    [Fact]
    public async Task RunPageOcrCommand_WhenFails_ShouldSetErrorMessage()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var ocrStore = new InMemoryOcrStore();
        var provider = new FakeOcrProvider
        {
            PageResultFunc = _ => throw new InvalidOperationException("Worker crashed")
        };
        var ocrService = new OcrService(provider, ocrStore, regionStore);

        var viewModel = new VisualEditorViewModel(
            workspace,
            new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            new PreviewLoaderStub(),
            ocrService);

        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);
        await viewModel.RunPageOcrCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOcrRunning);
        Assert.NotEmpty(viewModel.ErrorMessage);
        Assert.Equal("OCR failed.", viewModel.StatusMessage);
    }

    private ProjectWorkspace CreateWorkspace()
        => new(
            new TranslationProject(Guid.NewGuid(), "Project", "Series", Timestamp, Timestamp),
            _tempDirectory,
            Path.Combine(_tempDirectory, "Data"));

    private PageWorkspace CreatePage(int number)
    {
        string filePath = Path.Combine(_tempDirectory, $"{number}.png");
        if (!File.Exists(filePath))
        {
            File.WriteAllBytes(filePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        }

        var page = new Page(
            Guid.NewGuid(), Guid.NewGuid(), number, $"{number}.png", $"{number}.png",
            new string('A', 64), 1080, 15000);
        return new PageWorkspace(page, filePath);
    }

    private sealed class ChapterStoreStub(params PageWorkspace[] pages) : IChapterStore
    {
        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace, Chapter chapter, ChapterSource source,
            IProgress<ChapterImportProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>([]);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>(pages);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(pages.Single(page => page.Page.Id == pageId));

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterDirectory,
            ChapterNumber chapterNumber,
            string? title = null,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, now, now);
            return Task.FromResult(new ChapterIndexResult(workspace, chapter, pages));
        }
    }

    private sealed class PreviewLoaderStub : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 7500, rawPixelWidth, rawPixelHeight));

        public Task<PagePreview> LoadImageAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 7500, 1080, 15000));
    }

    private sealed class FakeOcrProvider : IOcrProvider
    {
        public Func<string, OcrPageResult>? PageResultFunc { get; set; }

        public Func<string, TextRegionGeometry, OcrRegionResult>? RegionResultFunc { get; set; }

        public Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken)
            => Task.FromResult(PageResultFunc is not null
                ? PageResultFunc(imagePath)
                : new OcrPageResult([], new OcrEngineMetadata("PaddleOCR", "3.7.0", "det", "rec", "Korean + English")));

        public Task<OcrRegionResult> RecognizeRegionAsync(string imagePath, TextRegionGeometry geometry, CancellationToken cancellationToken)
            => Task.FromResult(RegionResultFunc is not null
                ? RegionResultFunc(imagePath, geometry)
                : new OcrRegionResult("Default", 1.0, new OcrEngineMetadata("PaddleOCR", "3.7.0", "det", "rec", "Korean + English")));
    }

    private sealed class InMemoryOcrStore : IOcrStore
    {
        private readonly Dictionary<Guid, OcrRecognition> _recognitions = [];
        private readonly List<TextRegion> _regions = [];

        public Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, OcrRecognition>>(_recognitions);

        public Task<OcrRecognition?> GetRecognitionForRegionAsync(
            ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
            => Task.FromResult(_recognitions.GetValueOrDefault(regionId));

        public Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(
            ProjectWorkspace workspace,
            Guid pageId,
            IReadOnlyList<OcrDetectedRegion> detectedRegions,
            OcrEngineMetadata engineMetadata,
            CancellationToken cancellationToken)
        {
            _regions.RemoveAll(r => r.PageId == pageId && r.Origin == TextRegionOrigin.OcrDetection);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int order = 1;
            var created = new List<TextRegion>();
            foreach (OcrDetectedRegion d in detectedRegions)
            {
                var region = new TextRegion(
                    Guid.NewGuid(), pageId, d.Geometry, order++, TextRegionRole.Unknown, TextContainerType.Unknown,
                    now, now, TextRegionOrigin.OcrDetection);
                _regions.Add(region);
                created.Add(region);
                var rec = new OcrRecognition(
                    Guid.NewGuid(), region.Id, d.Text, d.RecognitionConfidence, d.DetectionConfidence,
                    engineMetadata.EngineName, engineMetadata.EngineVersion, engineMetadata.DetectionModel,
                    engineMetadata.RecognitionModel, engineMetadata.ModelProfile, now, now);
                _recognitions[region.Id] = rec;
            }

            return Task.FromResult<IReadOnlyList<TextRegion>>(_regions);
        }

        public Task<OcrRecognition> SaveRegionRecognitionAsync(
            ProjectWorkspace workspace,
            Guid regionId,
            OcrRegionResult regionResult,
            CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var rec = new OcrRecognition(
                Guid.NewGuid(), regionId, regionResult.Text, regionResult.RecognitionConfidence, null,
                regionResult.EngineInfo.EngineName, regionResult.EngineInfo.EngineVersion,
                regionResult.EngineInfo.DetectionModel, regionResult.EngineInfo.RecognitionModel,
                regionResult.EngineInfo.ModelProfile, now, now);
            _recognitions[regionId] = rec;
            return Task.FromResult(rec);
        }
    }
}
