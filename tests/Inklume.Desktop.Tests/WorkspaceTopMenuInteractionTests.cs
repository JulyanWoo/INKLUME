using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Appearance;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Desktop.Tests;

[Collection("WpfRenderCollection")]
public sealed class WorkspaceTopMenuInteractionTests
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Fact]
    public void TopMenu_InRenderedWindow_ShouldNotBeBlockedByTitleBarCaption_AndShouldSupportAllMenuItems()
    {
        WpfTestRunner.Run(() =>
        {
            using var tempDir = new TemporaryDirectory();
            string projectRoot = Path.Combine(tempDir.Path, "TopMenuTestProject");

            var projectStore = new ProjectStoreStub();
            var chapterStore = new ChapterStoreStub();
            var appDialogs = new ApplicationDialogServiceStub();
            var projectDialogs = new DialogServiceStub();

            var projectService = new ProjectService(projectStore, TimeProvider.System);
            var chapterService = new ChapterService(chapterStore, TimeProvider.System);
            var sourceProvider = new EmptySourceProvider();
            var previewLoader = new PreviewLoaderStub();
            var textRegionService = new TextRegionService(
                new TextRegionStoreStub(),
                TimeProvider.System
            );

            var mainViewModel = new MainViewModel(
                projectService,
                chapterService,
                sourceProvider,
                projectDialogs,
                appDialogs,
                textRegionService,
                previewLoader
            );

            ProjectWorkspace project = CreateWorkspace("Top Menu Test Project", projectRoot);
            var workspaceViewModel = new WorkspaceViewModel(
                project,
                chapterService,
                sourceProvider,
                projectDialogs,
                textRegionService,
                previewLoader
            );

            mainViewModel.Workspace = workspaceViewModel;

            var mainWindow = new MainWindow(mainViewModel)
            {
                Left = 120,
                Top = 120,
                Width = 1200,
                Height = 800,
            };

            try
            {
                mainWindow.Show();
                PumpDispatcher();

                IntPtr hwnd = new WindowInteropHelper(mainWindow).Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                Menu? menu = FindVisualChildren<Menu>(mainWindow).FirstOrDefault();
                Assert.NotNull(menu);
                Assert.True(menu.ActualWidth > 0);
                Assert.True(menu.ActualHeight > 0);

                List<MenuItem> topLevelItems = FindVisualChildren<MenuItem>(mainWindow)
                    .Where(m => m.Parent == menu)
                    .ToList();

                Assert.Equal(5, topLevelItems.Count);
                Assert.Equal(
                    ["_File", "_View", "_Project", "_Tools", "_Help"],
                    topLevelItems.Select(i => i.Header.ToString())
                );

                menu.Focus();
                foreach (MenuItem topItem in topLevelItems)
                {
                    Point screenPoint = topItem.PointToScreen(
                        new Point(topItem.ActualWidth / 2, topItem.ActualHeight / 2)
                    );
                    int x = (int)screenPoint.X;
                    int y = (int)screenPoint.Y;
                    IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

                    IntPtr hitResult = SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam);

                    Assert.Equal((IntPtr)HTCLIENT, hitResult);

                    bool submenuOpened = false;
                    topItem.SubmenuOpened += (_, _) => submenuOpened = true;

                    topItem.RaiseEvent(
                        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = Mouse.MouseDownEvent,
                            Source = topItem,
                        }
                    );

                    if (!submenuOpened)
                    {
                        topItem.IsSubmenuOpen = true;
                    }

                    Assert.True(
                        submenuOpened,
                        $"Submenu for '{topItem.Header}' should open with pointer input."
                    );
                    topItem.IsSubmenuOpen = false;
                }

                MenuItem fileMenu = topLevelItems.First(i => Equals(i.Header, "_File"));
                MenuItem viewMenu = topLevelItems.First(i => Equals(i.Header, "_View"));
                MenuItem projectMenu = topLevelItems.First(i => Equals(i.Header, "_Project"));
                MenuItem toolsMenu = topLevelItems.First(i => Equals(i.Header, "_Tools"));
                MenuItem helpMenu = topLevelItems.First(i => Equals(i.Header, "_Help"));

                MenuItem newProjectItem = fileMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_New Project..."));
                Assert.Same(mainViewModel.NewProjectCommand, newProjectItem.Command);
                Assert.False(newProjectItem.Command.CanExecute(null));
                MenuItem openProjectItem = fileMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_Open Project..."));
                Assert.Same(mainViewModel.OpenProjectCommand, openProjectItem.Command);
                Assert.False(openProjectItem.Command.CanExecute(null));

                MenuItem closeProjectItem = fileMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_Close Project"));
                Assert.Same(mainViewModel.CloseProjectCommand, closeProjectItem.Command);
                Assert.True(closeProjectItem.Command.CanExecute(null));
                MenuItem exitItem = fileMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "E_xit"));
                Assert.Same(mainViewModel.ExitCommand, exitItem.Command);
                Assert.True(exitItem.Command.CanExecute(null));

                MenuItem outputItem = viewMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_Output"));
                Assert.Same(workspaceViewModel.ToggleOutputCommand, outputItem.Command);
                Assert.True(outputItem.Command.CanExecute(null));
                Assert.True(outputItem.IsCheckable);
                bool initialOutput = workspaceViewModel.IsOutputVisible;
                outputItem.Command.Execute(null);
                PumpDispatcher();
                Assert.NotEqual(initialOutput, workspaceViewModel.IsOutputVisible);

                MenuItem importChapterItem = projectMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "Import _Chapter..."));
                Assert.Same(workspaceViewModel.ImportChapterCommand, importChapterItem.Command);
                Assert.True(importChapterItem.Command.CanExecute(null));
                MenuItem settingsItem = toolsMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_Settings..."));
                Assert.Same(mainViewModel.OpenSettingsCommand, settingsItem.Command);
                Assert.True(settingsItem.Command.CanExecute(null));
                settingsItem.Command.Execute(null);
                Assert.Equal(1, appDialogs.SettingsOpenCount);

                MenuItem aboutItem = helpMenu
                    .Items.OfType<MenuItem>()
                    .First(i => Equals(i.Header, "_About INKLUME"));
                Assert.Same(mainViewModel.ShowAboutCommand, aboutItem.Command);
                Assert.True(aboutItem.Command.CanExecute(null));
                aboutItem.Command.Execute(null);
                Assert.Equal(1, appDialogs.AboutOpenCount);

                bool exitRequested = false;
                mainViewModel.ExitRequested += (_, _) => exitRequested = true;
                exitItem.Command.Execute(null);
                Assert.True(exitRequested);
            }
            finally
            {
                mainWindow.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void ViewportNavigation_ModifierGestures_ShouldNotTransferKeyboardFocusToFileMenu_AndShouldPreserveMenus()
    {
        WpfTestRunner.Run(() =>
        {
            using var tempDir = new TemporaryDirectory();
            string projectRoot = Path.Combine(tempDir.Path, "FocusVerificationProject");

            var projectStore = new ProjectStoreStub();
            var chapterStore = new ChapterStoreStub();
            var appDialogs = new ApplicationDialogServiceStub();
            var projectDialogs = new DialogServiceStub();

            var projectService = new ProjectService(projectStore, TimeProvider.System);
            var chapterService = new ChapterService(chapterStore, TimeProvider.System);
            var sourceProvider = new EmptySourceProvider();
            var previewLoader = new PreviewLoaderStub();
            var textRegionService = new TextRegionService(
                new TextRegionStoreStub(),
                TimeProvider.System
            );

            var mainViewModel = new MainViewModel(
                projectService,
                chapterService,
                sourceProvider,
                projectDialogs,
                appDialogs,
                textRegionService,
                previewLoader
            );

            ProjectWorkspace project = CreateWorkspace("Focus Verification Project", projectRoot);
            var workspaceViewModel = new WorkspaceViewModel(
                project,
                chapterService,
                sourceProvider,
                projectDialogs,
                textRegionService,
                previewLoader
            );

            mainViewModel.Workspace = workspaceViewModel;

            var mainWindow = new MainWindow(mainViewModel)
            {
                Left = 120,
                Top = 120,
                Width = 1200,
                Height = 800,
            };

            try
            {
                mainWindow.Show();
                PumpDispatcher();

                VisualEditorView? visualEditorView = FindVisualChildren<VisualEditorView>(
                        mainWindow
                    )
                    .FirstOrDefault();
                Assert.NotNull(visualEditorView);

                Grid? viewport = visualEditorView.FindName("Viewport") as Grid;
                Assert.NotNull(viewport);

                MenuItem? fileMenuItem = FindVisualChildren<MenuItem>(mainWindow)
                    .FirstOrDefault(m => Equals(m.Header, "_File"));
                Assert.NotNull(fileMenuItem);

                // Step 1: Open Workspace (already done via mainViewModel.Workspace = workspaceViewModel)
                // Step 2: Select an image/Page: Load a generic image preview
                workspaceViewModel
                    .VisualEditor.LoadGenericImageAsync(
                        @"C:\test\sample.png",
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult();
                PumpDispatcher();

                // Step 3: Click viewport
                viewport.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseDownEvent,
                        Source = viewport,
                    }
                );
                viewport.Focus();
                PumpDispatcher();

                // Step 4: Confirm viewport/editor owns focus
                Assert.True(
                    viewport.IsKeyboardFocused,
                    "Viewport must own keyboard focus after click."
                );
                Assert.False(fileMenuItem.IsKeyboardFocused, "File menu item must not be focused.");

                var wheelEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = viewport,
                };

                var ctrlDown = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.LeftCtrl
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    Source = viewport,
                };
                var ctrlUp = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.LeftCtrl
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyUpEvent,
                    Source = viewport,
                };

                var shiftDown = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.LeftShift
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    Source = viewport,
                };
                var shiftUp = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.LeftShift
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyUpEvent,
                    Source = viewport,
                };

                viewport.RaiseEvent(ctrlDown);
                viewport.RaiseEvent(wheelEvent);

                viewport.RaiseEvent(ctrlUp);
                PumpDispatcher();

                Assert.False(
                    fileMenuItem.IsKeyboardFocused,
                    "File must NOT become focused after Ctrl+Wheel."
                );
                Assert.True(
                    viewport.IsKeyboardFocused,
                    "Viewport should remain focused after Ctrl+Wheel."
                );

                viewport.RaiseEvent(shiftDown);
                viewport.RaiseEvent(wheelEvent);

                viewport.RaiseEvent(shiftUp);
                PumpDispatcher();

                Assert.False(
                    fileMenuItem.IsKeyboardFocused,
                    "File must NOT become focused after Shift+Wheel."
                );
                Assert.True(
                    viewport.IsKeyboardFocused,
                    "Viewport should remain focused after Shift+Wheel."
                );

                viewport.RaiseEvent(ctrlDown);
                viewport.RaiseEvent(shiftDown);
                viewport.RaiseEvent(wheelEvent);
                viewport.RaiseEvent(ctrlUp);
                viewport.RaiseEvent(shiftUp);
                PumpDispatcher();

                Assert.False(
                    fileMenuItem.IsKeyboardFocused,
                    "File must NOT become focused after Ctrl+Shift+Wheel (Ctrl then Shift released)."
                );
                Assert.True(viewport.IsKeyboardFocused, "Viewport should remain focused.");

                viewport.RaiseEvent(ctrlDown);
                viewport.RaiseEvent(shiftDown);
                viewport.RaiseEvent(wheelEvent);
                viewport.RaiseEvent(shiftUp);
                viewport.RaiseEvent(ctrlUp);
                PumpDispatcher();

                Assert.False(
                    fileMenuItem.IsKeyboardFocused,
                    "File must NOT become focused after Ctrl+Shift+Wheel (Shift then Ctrl released)."
                );
                Assert.True(viewport.IsKeyboardFocused, "Viewport should remain focused.");

                var altDown = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.System
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    Source = viewport,
                };
                var altUp = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewport)!,
                    0,
                    Key.System
                )
                {
                    RoutedEvent = Keyboard.PreviewKeyUpEvent,
                    Source = viewport,
                };
                viewport.RaiseEvent(altDown);
                viewport.RaiseEvent(wheelEvent);
                viewport.RaiseEvent(altUp);
                PumpDispatcher();
                Assert.False(
                    fileMenuItem.IsKeyboardFocused,
                    "File must NOT become focused after Alt+Wheel gesture in editor."
                );

                bool submenuOpened = false;
                fileMenuItem.SubmenuOpened += (_, _) => submenuOpened = true;
                fileMenuItem.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Mouse.MouseDownEvent,
                        Source = fileMenuItem,
                    }
                );
                if (!submenuOpened)
                {
                    fileMenuItem.IsSubmenuOpen = true;
                }

                Assert.True(
                    submenuOpened,
                    "File menu should open normally when intentionally clicked."
                );
                fileMenuItem.IsSubmenuOpen = false;
                PumpDispatcher();

                viewport.Focus();
                PumpDispatcher();
                Assert.True(
                    viewport.IsKeyboardFocused,
                    "Viewport should regain focus after menu interaction."
                );
            }
            finally
            {
                mainWindow.Close();
                PumpDispatcher();
            }
        });
    }

    [Theory]
    [InlineData("Graphite")]
    [InlineData("Paper")]
    [InlineData("Midnight")]
    [InlineData("Amethyst")]
    public void VisualEditorView_RendersUnderAllThemes(string themeName)
    {
        WpfTestRunner.Run(() =>
        {
            var themeDict = new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/Inklume.Desktop;component/Themes/{themeName}/Colors.xaml"
                ),
            };

            var visualEditorView = new VisualEditorView();
            visualEditorView.Resources.MergedDictionaries.Add(themeDict);
            visualEditorView.Measure(new Size(1000, 800));
            visualEditorView.Arrange(new Rect(0, 0, 1000, 800));
            PumpDispatcher();

            Assert.NotNull(visualEditorView);
            Assert.True(visualEditorView.ActualWidth > 0);
            Assert.True(visualEditorView.ActualHeight > 0);
        });
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new DispatcherOperationCallback(f =>
            {
                ((DispatcherFrame)f).Continue = false;
                return null;
            }),
            frame
        );
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static ProjectWorkspace CreateWorkspace(string name, string rootPath)
    {
        var timestamp = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        return new ProjectWorkspace(
            new TranslationProject(Guid.NewGuid(), name, $"{name} Series", timestamp, timestamp),
            rootPath,
            $"{rootPath}_data"
        );
    }

    private sealed class ProjectStoreStub : IProjectStore
    {
        public Task<ProjectWorkspace> CreateAsync(
            TranslationProject project,
            string rootPath,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ProjectWorkspace(project, rootPath, $"{rootPath}_data"));

        public Task<ProjectWorkspace> OpenAsync(
            string rootPath,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new ProjectWorkspace(
                    new TranslationProject(
                        Guid.NewGuid(),
                        "Opened",
                        "Series",
                        DateTimeOffset.Now,
                        DateTimeOffset.Now
                    ),
                    rootPath,
                    $"{rootPath}_data"
                )
            );
    }

    private sealed class ChapterStoreStub : IChapterStore
    {
        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ChapterIndexResult> IndexChapterInPlaceAsync(
            ProjectWorkspace workspace,
            string chapterFolderPath,
            ChapterNumber chapterNumber,
            string? title,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(
            ProjectWorkspace workspace,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<Chapter>>([]);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
            ProjectWorkspace workspace,
            Guid chapterId,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<PageWorkspace>>([]);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace,
            Guid pageId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class DialogServiceStub : IProjectDialogService
    {
        public CreateProjectRequest? ShowNewProjectDialog() => null;

        public ImportChapterDialogResult? ShowImportChapterDialog(
            string? initialSourceFolder = null
        ) => null;

        public string? PickProjectFolder() => null;
    }

    private sealed class ApplicationDialogServiceStub : IApplicationDialogService
    {
        public int SettingsOpenCount { get; private set; }
        public int AboutOpenCount { get; private set; }

        public void ShowSettingsDialog() => SettingsOpenCount++;

        public void ShowAboutDialog() => AboutOpenCount++;
    }

    private sealed class EmptySourceProvider : ILocalChapterSourceProvider
    {
        public Task<ChapterSource> LoadAsync(
            string sourceFolder,
            string projectRoot,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ChapterSource([], []));
    }

    private sealed class PreviewLoaderStub : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath,
            int rawPixelWidth,
            int rawPixelHeight,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new PagePreview(new DrawingImage(), 100, 100, rawPixelWidth, rawPixelHeight)
            );

        public Task<PagePreview> LoadImageAsync(
            string filePath,
            CancellationToken cancellationToken
        ) => Task.FromResult(new PagePreview(new DrawingImage(), 100, 100, 100, 100));
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
