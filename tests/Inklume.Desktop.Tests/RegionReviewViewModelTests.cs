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

public sealed class RegionReviewViewModelTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoSelectedRegion_ShouldDisableReviewControls()
    {
        var (editor, review, _, _, _) = CreateFixture();

        Assert.False(review.HasSelectedRegion);
        Assert.False(review.HasOcr);
        Assert.Equal("No OCR result", review.RawOcrText);
        Assert.Empty(review.ReviewedTextDraft);
        Assert.False(review.IsReviewTextDirty);
        Assert.False(review.SaveReviewedTextCommand.CanExecute(null));
        Assert.False(review.ResetReviewedTextCommand.CanExecute(null));
        Assert.False(review.MarkReviewedCommand.CanExecute(null));
        Assert.False(review.MarkPendingCommand.CanExecute(null));
        Assert.False(review.MoveUpCommand.CanExecute(null));
        Assert.False(review.MoveDownCommand.CanExecute(null));
        Assert.False(review.SelectPreviousCommand.CanExecute(null));
        Assert.False(review.SelectNextCommand.CanExecute(null));
        Assert.False(review.RecognizeRegionCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectRegion_ShouldLoadReviewState_AndInitializeDraft()
    {
        var (editor, review, regionStore, ocrStore, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await editor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        var recognition1 = await ocrStore.SaveRegionRecognitionAsync(
            editor.Workspace,
            region.Id,
            new OcrRegionResult("Machine OCR Text", 0.95, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);
        editor.SetRegionRecognition(region.Id, recognition1);

        // Select region
        editor.SelectRegion(region);

        Assert.True(review.HasSelectedRegion);
        Assert.True(review.HasOcr);
        Assert.Equal("Machine OCR Text", review.RawOcrText);
        Assert.Empty(review.ReviewedTextDraft);
        Assert.False(review.IsReviewTextDirty);
        Assert.Equal("Pending", review.ReviewStatusDisplay);
        Assert.Equal("Machine OCR Text", review.EffectiveSourceTextDisplay);
    }

    [Fact]
    public async Task ReviewedTextDraft_Editing_SetsDirtyState_AndSavePersists()
    {
        var (editor, review, _, ocrStore, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await editor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        var recognition2 = await ocrStore.SaveRegionRecognitionAsync(
            editor.Workspace,
            region.Id,
            new OcrRegionResult("Raw OCR", 0.95, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);
        editor.SetRegionRecognition(region.Id, recognition2);

        editor.SelectRegion(region);

        // Edit text
        review.ReviewedTextDraft = "Human Corrected Text";
        Assert.True(review.IsReviewTextDirty);
        Assert.True(review.SaveReviewedTextCommand.CanExecute(null));
        Assert.True(review.ResetReviewedTextCommand.CanExecute(null));

        // Save
        await review.SaveReviewedTextCommand.ExecuteAsync(null);

        Assert.False(review.IsReviewTextDirty);
        Assert.Equal("Human Corrected Text", review.ReviewedTextDraft);
        Assert.Equal("Human Corrected Text", review.EffectiveSourceTextDisplay);
        Assert.Equal("Raw OCR", review.RawOcrText); // Raw OCR must not be overwritten
    }

    [Fact]
    public async Task ResetReviewedTextCommand_ShouldClearReviewedOverride_AndRestoreEffectiveToRawOcr()
    {
        var (editor, review, _, ocrStore, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await editor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        var recognition3 = await ocrStore.SaveRegionRecognitionAsync(
            editor.Workspace,
            region.Id,
            new OcrRegionResult("Raw OCR", 0.95, CreateEngineMetadata()),
            TestContext.Current.CancellationToken);
        editor.SetRegionRecognition(region.Id, recognition3);

        editor.SelectRegion(region);
        review.ReviewedTextDraft = "Override Text";
        await review.SaveReviewedTextCommand.ExecuteAsync(null);

        Assert.Equal("Override Text", review.EffectiveSourceTextDisplay);

        // Reset
        await review.ResetReviewedTextCommand.ExecuteAsync(null);

        Assert.Empty(review.ReviewedTextDraft);
        Assert.False(review.IsReviewTextDirty);
        Assert.Equal("Raw OCR", review.EffectiveSourceTextDisplay);
        Assert.Equal("Raw OCR", review.RawOcrText);
    }

    [Fact]
    public async Task MarkReviewed_And_MarkPending_Commands_ShouldUpdateStatus()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await editor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        editor.SelectRegion(region);

        Assert.True(review.IsPending);
        Assert.False(review.IsReviewed);
        Assert.True(review.MarkReviewedCommand.CanExecute(null));
        Assert.False(review.MarkPendingCommand.CanExecute(null));

        await review.MarkReviewedCommand.ExecuteAsync(null);

        Assert.True(review.IsReviewed);
        Assert.False(review.IsPending);
        Assert.Equal("Reviewed", review.ReviewStatusDisplay);
        Assert.False(review.MarkReviewedCommand.CanExecute(null));
        Assert.True(review.MarkPendingCommand.CanExecute(null));

        await review.MarkPendingCommand.ExecuteAsync(null);

        Assert.True(review.IsPending);
        Assert.False(review.IsReviewed);
        Assert.Equal("Pending", review.ReviewStatusDisplay);
    }

    [Fact]
    public async Task UpdateClassificationAsync_ShouldUpdateRoleAndContainer()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await editor.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();

        editor.SelectRegion(region);

        await review.UpdateClassificationAsync(TextRegionRole.Dialogue, TextContainerType.SpeechBubble);

        Assert.Equal(TextRegionRole.Dialogue, review.SelectedRegion?.Role);
        Assert.Equal(TextContainerType.SpeechBubble, review.SelectedRegion?.ContainerType);
    }

    [Fact]
    public async Task MoveUp_And_MoveDown_ShouldUpdateOrderAndSelection()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion r1 = await editor.CreateRectangleAsync(new ImagePoint(10, 10), new ImagePoint(50, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        TextRegion r2 = await editor.CreateRectangleAsync(new ImagePoint(60, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        TextRegion r3 = await editor.CreateRectangleAsync(new ImagePoint(10, 60), new ImagePoint(50, 100), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        // Select r3 (order 3)
        editor.SelectRegion(r3);
        Assert.True(review.MoveUpCommand.CanExecute(null));
        Assert.False(review.MoveDownCommand.CanExecute(null));

        // Move r3 up -> swaps with r2
        await review.MoveUpCommand.ExecuteAsync(null);
        Assert.Equal(2, review.SelectedRegion?.ReadingOrder);

        // Select r1 (order 1)
        editor.SelectRegion(r1);
        Assert.False(review.MoveUpCommand.CanExecute(null));
        Assert.True(review.MoveDownCommand.CanExecute(null));

        // Move r1 down -> swaps with r3
        await review.MoveDownCommand.ExecuteAsync(null);
        Assert.Equal(2, review.SelectedRegion?.ReadingOrder);
    }

    [Fact]
    public async Task Previous_And_Next_Navigation_ShouldCycleThroughRegions()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion r1 = await editor.CreateRectangleAsync(new ImagePoint(10, 10), new ImagePoint(50, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        TextRegion r2 = await editor.CreateRectangleAsync(new ImagePoint(60, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        editor.SelectRegion(r1);
        Assert.False(review.SelectPreviousCommand.CanExecute(null));
        Assert.True(review.SelectNextCommand.CanExecute(null));

        // Navigate Next
        review.SelectNextCommand.Execute(null);
        Assert.Equal(r2.Id, editor.SelectedRegion?.Id);
        Assert.True(review.SelectPreviousCommand.CanExecute(null));
        Assert.False(review.SelectNextCommand.CanExecute(null));

        // Navigate Previous
        review.SelectPreviousCommand.Execute(null);
        Assert.Equal(r1.Id, editor.SelectedRegion?.Id);
    }

    [Fact]
    public async Task AutoCommitDraft_WhenChangingRegionSelection_ShouldNotLoseEdits()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion r1 = await editor.CreateRectangleAsync(new ImagePoint(10, 10), new ImagePoint(50, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        TextRegion r2 = await editor.CreateRectangleAsync(new ImagePoint(60, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        editor.SelectRegion(r1);
        review.ReviewedTextDraft = "Unsaved Draft For R1";
        Assert.True(review.IsReviewTextDirty);

        // Switch to r2 asynchronously (which invokes CommitDraftIfDirtyAsync)
        bool selected = await editor.SelectRegionAsync(r2);
        Assert.True(selected);
        Assert.Equal(r2.Id, editor.SelectedRegion?.Id);

        // Switch back to r1: verify draft was committed and preserved!
        await editor.SelectRegionAsync(r1);
        Assert.Equal("Unsaved Draft For R1", review.ReviewedTextDraft);
        Assert.False(review.IsReviewTextDirty);
    }

    [Fact]
    public async Task PageReviewProgress_ShouldReflectReviewedCount()
    {
        var (editor, review, _, _, page) = CreateFixture();
        await editor.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion r1 = await editor.CreateRectangleAsync(new ImagePoint(10, 10), new ImagePoint(50, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();
        TextRegion r2 = await editor.CreateRectangleAsync(new ImagePoint(60, 10), new ImagePoint(100, 50), TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        editor.SelectRegion(r1);
        Assert.Equal("Reviewed 0 / 2", review.PageReviewProgressDisplay);

        await review.MarkReviewedCommand.ExecuteAsync(null);
        Assert.Equal("Reviewed 1 / 2", review.PageReviewProgressDisplay);
    }

    private static (VisualEditorViewModel Editor, RegionReviewViewModel Review, TextRegionStoreStub RegionStore, InMemoryOcrStore OcrStore, PageWorkspace Page) CreateFixture()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var ocrStore = new InMemoryOcrStore();

        var textRegionService = new TextRegionService(regionStore, TimeProvider.System);
        var reviewService = new TextRegionReviewService(regionStore, ocrStore, TimeProvider.System);
        var ocrProvider = new FakeOcrProvider();
        var ocrService = new OcrService(ocrProvider, ocrStore, regionStore);

        var editor = new VisualEditorViewModel(
            workspace,
            new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            textRegionService,
            new PreviewLoaderStub(),
            ocrService,
            reviewService);

        return (editor, editor.Review, regionStore, ocrStore, page);
    }

    private static ProjectWorkspace CreateWorkspace()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Manga", "Series", Timestamp, Timestamp);
        return new ProjectWorkspace(project, @"C:\fake\source", @"C:\fake\data");
    }

    private static PageWorkspace CreatePage(int pageNumber)
    {
        var chapter = new Chapter(Guid.NewGuid(), Guid.NewGuid(), new ChapterNumber(1), "Ch1", Timestamp, Timestamp);
        var page = new Page(Guid.NewGuid(), chapter.Id, pageNumber, $"{pageNumber:D2}.png", $"{pageNumber:D2}.png", new string('0', 64), 1000, 1500);
        return new PageWorkspace(page, $"{pageNumber:D2}.png");
    }

    private static OcrEngineMetadata CreateEngineMetadata()
        => new("PaddleOCR", "3.7.0", "det", "rec", "Korean + English");

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
        public Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken)
            => Task.FromResult(new OcrPageResult([], CreateEngineMetadata()));

        public Task<OcrRegionResult> RecognizeRegionAsync(string imagePath, TextRegionGeometry geometry, CancellationToken cancellationToken)
            => Task.FromResult(new OcrRegionResult("Recognized Text", 0.95, CreateEngineMetadata()));
    }

    private sealed class InMemoryOcrStore : IOcrStore
    {
        public Dictionary<Guid, OcrRecognition> Recognitions { get; } = [];

        public Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, OcrRecognition>>(Recognitions);

        public Task<OcrRecognition?> GetRecognitionForRegionAsync(ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
            => Task.FromResult(Recognitions.GetValueOrDefault(regionId));

        public Task<OcrRecognition> SaveRegionRecognitionAsync(ProjectWorkspace workspace, Guid regionId, OcrRegionResult result, CancellationToken cancellationToken)
        {
            var recognition = new OcrRecognition(
                Guid.NewGuid(), regionId, result.Text, result.RecognitionConfidence, null,
                result.EngineInfo.EngineName, result.EngineInfo.EngineVersion, result.EngineInfo.DetectionModel,
                result.EngineInfo.RecognitionModel, result.EngineInfo.ModelProfile, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Recognitions[regionId] = recognition;
            return Task.FromResult(recognition);
        }

        public Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(ProjectWorkspace workspace, Guid pageId, IReadOnlyList<OcrDetectedRegion> detectedRegions, OcrEngineMetadata engineMetadata, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TextRegion>>([]);
    }
}
