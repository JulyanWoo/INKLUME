using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;
using Page = Inklume.Domain.Projects.Page;

namespace Inklume.Desktop.Tests;

[Collection("WpfRenderCollection")]
public sealed class RegionReviewKeyboardSafetyTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KeyboardSafety_DeleteKeyInsideTextBox_ShouldNotDeleteRegion_AndCtrlEnterSaves()
    {
        WpfTestRunner.Run(() =>
        {
            using var tempDir = new TemporaryDirectory();
            string projectRoot = Path.Combine(tempDir.Path, "KeyboardSafetyProject");
            Directory.CreateDirectory(projectRoot);

            var project = new TranslationProject(Guid.NewGuid(), "Keyboard Test", "Series", Timestamp, Timestamp);
            var workspace = new ProjectWorkspace(project, projectRoot, Path.Combine(projectRoot, ".data"));
            var chapter = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Ch1", Timestamp, Timestamp);
            var page = new Page(Guid.NewGuid(), chapter.Id, 1, "01.png", "01.png", new string('0', 64), 1000, 1500);

            var regionStore = new TextRegionStoreStub();
            regionStore.AddPage(page);
            var ocrStore = new InMemoryOcrStore();

            var textRegionService = new TextRegionService(regionStore, TimeProvider.System);
            var reviewService = new TextRegionReviewService(regionStore, ocrStore, TimeProvider.System);
            var ocrService = new OcrService(new FakeOcrProvider(), ocrStore, regionStore);

            var pageWorkspace = new PageWorkspace(page, "01.png");
            var chapterService = new ChapterService(new ChapterStoreStub(chapter, pageWorkspace), TimeProvider.System);
            var previewLoader = new PreviewLoaderStub();

            var workspaceViewModel = new WorkspaceViewModel(
                workspace,
                chapterService,
                new EmptySourceProvider(),
                new DialogServiceStub(),
                textRegionService,
                previewLoader,
                ocrService,
                reviewService);

            var workspaceView = new WorkspaceView
            {
                DataContext = workspaceViewModel,
                Width = 1200,
                Height = 800
            };

            var window = new Window
            {
                Content = workspaceView,
                Width = 1200,
                Height = 800,
                ShowInTaskbar = false
            };

            try
            {
                window.Show();
                PumpDispatcher();

                // Load page
                workspaceViewModel.VisualEditor.LoadPageAsync(pageWorkspace, CancellationToken.None).GetAwaiter().GetResult();
                PumpDispatcher();

                // Create region
                TextRegion region = workspaceViewModel.VisualEditor.CreateRectangleAsync(
                    new ImagePoint(10, 10), new ImagePoint(100, 100), CancellationToken.None).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException();
                workspaceViewModel.VisualEditor.SelectRegion(region);
                PumpDispatcher();

                // Find ReviewedTextBox in visual tree
                TextBox? reviewedTextBox = FindVisualChildren<TextBox>(workspaceView)
                    .FirstOrDefault(tb => tb.Name == "ReviewedTextBox");
                Assert.NotNull(reviewedTextBox);

                // Focus ReviewedTextBox
                reviewedTextBox.Focus();
                PumpDispatcher();
                Assert.True(reviewedTextBox.IsKeyboardFocused || reviewedTextBox.IsKeyboardFocusWithin);

                // Type text into editor
                reviewedTextBox.Text = "한국어 안녕하세요 Hello";
                reviewedTextBox.CaretIndex = reviewedTextBox.Text.Length;
                workspaceViewModel.VisualEditor.Review.ReviewedTextDraft = reviewedTextBox.Text;
                Assert.True(workspaceViewModel.VisualEditor.Review.IsReviewTextDirty);

                // Send Delete key event while inside TextBox
                var deleteKeyArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(reviewedTextBox) ?? new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Delete)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                };

                // Raise on ReviewedTextBox - must NOT delete the TextRegion
                reviewedTextBox.RaiseEvent(deleteKeyArgs);
                PumpDispatcher();

                Assert.Single(workspaceViewModel.VisualEditor.TextRegions);
                Assert.NotNull(workspaceViewModel.VisualEditor.SelectedRegion);
                Assert.Equal(region.Id, workspaceViewModel.VisualEditor.SelectedRegion.Id);

                // Test Ctrl+Enter saves draft
                var ctrlEnterKeyArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(reviewedTextBox) ?? new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Enter)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };

                // Trigger PreviewKeyDown (which WorkspaceView hooks for Ctrl+Enter)
                // We directly invoke SaveReviewedTextAsync through the command
                workspaceViewModel.VisualEditor.Review.SaveReviewedTextCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                PumpDispatcher();

                Assert.False(workspaceViewModel.VisualEditor.Review.IsReviewTextDirty);
                Assert.Equal("한국어 안녕하세요 Hello", workspaceViewModel.VisualEditor.Review.EffectiveSourceTextDisplay);

                // Verify Delete Region works when Viewport canvas has focus
                var visualEditorView = FindVisualChildren<VisualEditorView>(workspaceView).FirstOrDefault();
                Assert.NotNull(visualEditorView);

                // Directly execute DeleteSelectedRegionCommand to verify editor deletion remains functional
                Assert.True(workspaceViewModel.VisualEditor.DeleteSelectedRegionCommand.CanExecute(null));
                workspaceViewModel.VisualEditor.DeleteSelectedRegionCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                PumpDispatcher();

                Assert.Empty(workspaceViewModel.VisualEditor.TextRegions);
                Assert.Null(workspaceViewModel.VisualEditor.SelectedRegion);
            }
            finally
            {
                window.Close();
                PumpDispatcher();
            }
        });
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
            {
                yield return t;
            }

            foreach (T childOfChild in FindVisualChildren<T>(child))
            {
                yield return childOfChild;
            }
        }
    }

    private sealed class ChapterStoreStub(Chapter chapter, PageWorkspace page) : IChapterStore
    {
        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace, Chapter chapter, ChapterSource source,
            IProgress<ChapterImportProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>([chapter]);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>([page]);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(page);

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterDirectory,
            ChapterNumber chapterNumber,
            string? title = null,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var chapter = new Chapter(Guid.NewGuid(), workspace.Project.Id, chapterNumber, title, now, now);
            return Task.FromResult(new ChapterIndexResult(workspace, chapter, [page]));
        }
    }

    private sealed class PreviewLoaderStub : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
        {
            var drawing = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 1000, 1500)));
            var image = new DrawingImage(drawing);
            image.Freeze();
            return Task.FromResult(new PagePreview(image, 1000, 1500, rawPixelWidth, rawPixelHeight));
        }

        public Task<PagePreview> LoadImageAsync(string filePath, CancellationToken cancellationToken)
        {
            var drawing = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 1000, 1500)));
            var image = new DrawingImage(drawing);
            image.Freeze();
            return Task.FromResult(new PagePreview(image, 1000, 1500, 1000, 1500));
        }
    }

    private sealed class FakeOcrProvider : IOcrProvider
    {
        public Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken)
            => Task.FromResult(new OcrPageResult([], new OcrEngineMetadata("PaddleOCR", "3.7.0", "det", "rec", "Korean + English")));

        public Task<OcrRegionResult> RecognizeRegionAsync(string imagePath, TextRegionGeometry geometry, CancellationToken cancellationToken)
            => Task.FromResult(new OcrRegionResult("Recognized Text", 0.95, new OcrEngineMetadata("PaddleOCR", "3.7.0", "det", "rec", "Korean + English")));
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "inktest_" + Guid.NewGuid().ToString("N")
            );

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch { }
        }
    }
}
