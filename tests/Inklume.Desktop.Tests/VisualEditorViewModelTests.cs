using System.Windows.Media;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.VisualEditor;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class VisualEditorViewModelTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadPageAsync_ShouldLoadOnlySelectedPageAndClearTransientSelection()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace first = CreatePage(1);
        PageWorkspace second = CreatePage(2);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(first.Page);
        regionStore.AddPage(second.Page);
        var service = new TextRegionService(regionStore, TimeProvider.System);
        var viewModel = new VisualEditorViewModel(
            workspace, new ChapterService(new ChapterStoreStub(first, second), TimeProvider.System),
            service, new PreviewLoaderStub());
        await viewModel.LoadPageAsync(first, TestContext.Current.CancellationToken);
        TextRegion firstRegion = await viewModel.CreateRectangleAsync(
            new ImagePoint(10, 10), new ImagePoint(100, 100), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();
        viewModel.RectangleToolCommand.Execute(null);

        await viewModel.LoadPageAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(second.Page.Id, viewModel.SelectedPage?.Page.Id);
        Assert.Empty(viewModel.TextRegions);
        Assert.Null(viewModel.SelectedRegion);
        Assert.Equal(VisualEditorTool.Select, viewModel.SelectedTool);
        Assert.NotEqual(firstRegion.PageId, viewModel.SelectedPage?.Page.Id);
    }

    [Fact]
    public async Task RegionCommands_ShouldCreateSelectUpdateAndDeleteCanonicalRegion()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var viewModel = new VisualEditorViewModel(
            workspace, new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System), new PreviewLoaderStub());
        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);

        TextRegion region = await viewModel.CreateRectangleAsync(
            new ImagePoint(20, 30), new ImagePoint(120, 160), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException();
        await viewModel.UpdateSelectedClassificationAsync(
            TextRegionRole.Narration, TextContainerType.NarrationBox, TestContext.Current.CancellationToken);
        await viewModel.DeleteSelectedRegionCommand.ExecuteAsync(null);

        Assert.Equal(1, region.ReadingOrder);
        Assert.Empty(viewModel.TextRegions);
        Assert.Null(viewModel.SelectedRegion);
    }

    [Fact]
    public async Task ViewportCommands_ShouldFitResetAndUseCursorCenteredZoom()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var viewModel = new VisualEditorViewModel(
            workspace, new ChapterService(new ChapterStoreStub(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System), new PreviewLoaderStub());
        await viewModel.LoadPageAsync(page, TestContext.Current.CancellationToken);
        viewModel.ConfigureViewport(1000, 700, fitIfUninitialized: true);
        double fittedZoom = viewModel.ZoomScale;

        viewModel.ZoomAt(new ViewportPoint(500, 350), 120);
        viewModel.PanBy(20, -20);
        viewModel.ResetZoomCommand.Execute(null);

        Assert.True(fittedZoom < ViewportTransform.MinimumInteractiveZoom);
        Assert.Equal(1, viewModel.ZoomScale);
        Assert.Equal("100%", viewModel.ZoomPercentDisplay);
    }

    private static ProjectWorkspace CreateWorkspace()
        => new(
            new TranslationProject(Guid.NewGuid(), "Project", "Series", Timestamp, Timestamp),
            @"C:\Project");

    private static PageWorkspace CreatePage(int number)
    {
        var page = new Page(
            Guid.NewGuid(), Guid.NewGuid(), number, $"{number}.png", $"{number}.png",
            new string('A', 64), 1080, 15000);
        return new PageWorkspace(page, $@"C:\Project\{number}.png");
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
    }

    private sealed class PreviewLoaderStub : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 7500));
    }
}
