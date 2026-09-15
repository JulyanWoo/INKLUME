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

public sealed class DesktopFolderFirstAndZoomTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GenericImage_ZoomInCommand_ProducesVisibleZoomIncrease()
    {
        VisualEditorViewModel visualEditor = CreateVisualEditor();
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\cover.png", CancellationToken.None);
        visualEditor.ConfigureViewport(1000, 800, fitIfUninitialized: true);

        double initialZoom = visualEditor.ZoomScale;
        Assert.True(visualEditor.ZoomInCommand.CanExecute(null));

        visualEditor.ZoomInCommand.Execute(null);

        Assert.True(visualEditor.ZoomScale > initialZoom,
            $"Expected zoom to increase after ZoomIn, but was {visualEditor.ZoomScale} (initial: {initialZoom})");
    }

    [Fact]
    public async Task GenericImage_ZoomOutCommand_ProducesVisibleZoomDecrease()
    {
        VisualEditorViewModel visualEditor = CreateVisualEditor();
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\cover.png", CancellationToken.None);
        visualEditor.ConfigureViewport(1000, 800, fitIfUninitialized: true);

        // Zoom in first so we are well above minimum interactive zoom
        visualEditor.ZoomInCommand.Execute(null);
        visualEditor.ZoomInCommand.Execute(null);
        double beforeZoomOut = visualEditor.ZoomScale;

        Assert.True(visualEditor.ZoomOutCommand.CanExecute(null));
        visualEditor.ZoomOutCommand.Execute(null);

        Assert.True(visualEditor.ZoomScale < beforeZoomOut,
            $"Expected zoom to decrease after ZoomOut, but was {visualEditor.ZoomScale} (before: {beforeZoomOut})");
    }

    [Fact]
    public async Task GenericImage_ResetZoomCommand_Restores100PercentZoom()
    {
        VisualEditorViewModel visualEditor = CreateVisualEditor();
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\cover.png", CancellationToken.None);
        visualEditor.ConfigureViewport(1000, 800, fitIfUninitialized: true);

        visualEditor.ZoomInCommand.Execute(null);
        visualEditor.ZoomInCommand.Execute(null);
        Assert.NotEqual(1.0, visualEditor.ZoomScale);

        Assert.True(visualEditor.ResetZoomCommand.CanExecute(null));
        visualEditor.ResetZoomCommand.Execute(null);

        Assert.Equal(1.0, visualEditor.ZoomScale);
    }

    [Fact]
    public async Task GenericImage_FitToViewCommand_FitsImageToViewport()
    {
        VisualEditorViewModel visualEditor = CreateVisualEditor();
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\cover.png", CancellationToken.None);
        visualEditor.ConfigureViewport(1000, 800, fitIfUninitialized: true);

        double fitZoom = visualEditor.ZoomScale;

        // Change zoom away from fit
        visualEditor.ResetZoomCommand.Execute(null);
        Assert.Equal(1.0, visualEditor.ZoomScale);

        Assert.True(visualEditor.FitToViewCommand.CanExecute(null));
        visualEditor.FitToViewCommand.Execute(null);

        Assert.Equal(fitZoom, visualEditor.ZoomScale, precision: 4);
    }

    [Fact]
    public async Task GenericImage_ZoomAt_And_PanBy_ModifyViewportTransformOffsetAndScale()
    {
        VisualEditorViewModel visualEditor = CreateVisualEditor();
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\cover.png", CancellationToken.None);
        visualEditor.ConfigureViewport(1000, 800, fitIfUninitialized: true);

        double initialZoom = visualEditor.ZoomScale;
        double initialOffsetX = visualEditor.ViewportOffsetX;
        double initialOffsetY = visualEditor.ViewportOffsetY;

        // Wheel zoom at cursor point
        var cursor = new ViewportPoint(500, 400);
        visualEditor.ZoomAt(cursor, wheelDelta: 120);
        Assert.True(visualEditor.ZoomScale > initialZoom);

        // Pan viewport
        visualEditor.PanBy(horizontalDelta: 50, verticalDelta: -30);
        Assert.NotEqual(initialOffsetX, visualEditor.ViewportOffsetX);
        Assert.NotEqual(initialOffsetY, visualEditor.ViewportOffsetY);
    }

    [Fact]
    public async Task GenericImage_RegionEditingTools_Disabled_AndEnabledForCanonicalPage()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        PageWorkspace page = CreatePage(1);
        var regionStore = new TextRegionStoreStub();
        regionStore.AddPage(page.Page);
        var previewLoader = new MockPreviewLoader();
        var visualEditor = new VisualEditorViewModel(
            workspace,
            new ChapterService(new MockChapterStore(page), TimeProvider.System),
            new TextRegionService(regionStore, TimeProvider.System),
            previewLoader);

        // 1. Generic Image Preview -> Tools disabled
        await visualEditor.LoadGenericImageAsync(@"C:\Project\references\sample.png", CancellationToken.None);
        Assert.True(visualEditor.IsGenericPreview);
        Assert.False(visualEditor.IsEditingEnabled);
        Assert.False(visualEditor.RectangleToolCommand.CanExecute(null));
        Assert.False(visualEditor.PolygonToolCommand.CanExecute(null));
        Assert.False(visualEditor.DeleteSelectedRegionCommand.CanExecute(null));

        // 2. Canonical Page -> Tools enabled
        await visualEditor.LoadPageAsync(page, CancellationToken.None);
        Assert.False(visualEditor.IsGenericPreview);
        Assert.True(visualEditor.IsEditingEnabled);
        Assert.True(visualEditor.RectangleToolCommand.CanExecute(null));
        Assert.True(visualEditor.PolygonToolCommand.CanExecute(null));
    }

    private static VisualEditorViewModel CreateVisualEditor()
    {
        ProjectWorkspace workspace = CreateWorkspace();
        var previewLoader = new MockPreviewLoader();
        return new VisualEditorViewModel(
            workspace,
            new ChapterService(new MockChapterStore(), TimeProvider.System),
            new TextRegionService(new TextRegionStoreStub(), TimeProvider.System),
            previewLoader);
    }

    private static ProjectWorkspace CreateWorkspace()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Test Project", "Test Series", Timestamp, Timestamp);
        return new ProjectWorkspace(project, @"C:\Project", @"C:\Project_data");
    }

    private static PageWorkspace CreatePage(int number)
    {
        var page = new Page(
            Guid.NewGuid(),
            Guid.NewGuid(),
            number,
            $"{number:D2}.png",
            $"001/{number:D2}.png",
            new string('A', 64),
            800,
            1200);

        return new PageWorkspace(page, $@"C:\Project\001\{number:D2}.png");
    }

    private sealed class MockPreviewLoader : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, rawPixelWidth, rawPixelHeight));

        public Task<PagePreview> LoadImageAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 540, 960, 1080, 1920));
    }

    private sealed class MockChapterStore : IChapterStore
    {
        private readonly List<Chapter> _chapters = [];
        private readonly List<PageWorkspace> _pages = [];

        public MockChapterStore()
        {
        }

        public MockChapterStore(params PageWorkspace[] pages)
        {
            _pages.AddRange(pages);
        }

        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken)
            => Task.FromResult(new ChapterImportResult(workspace, chapter, [], []));

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterFolderPath,
            ChapterNumber chapterNumber,
            string? title,
            CancellationToken cancellationToken)
            => Task.FromResult(new ChapterIndexResult(workspace, new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), []));

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>(_chapters);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>(_pages.Where(p => p.Page.ChapterId == chapterId).ToArray());

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(_pages.Single(p => p.Page.Id == pageId));
    }
}
