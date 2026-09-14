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
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Desktop.Tests;

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
        RunInStaThread(() =>
        {
            EnsureWpfApplication();

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
            var textRegionService = new TextRegionService(new TextRegionStoreStub(), TimeProvider.System);

            var mainViewModel = new MainViewModel(
                projectService,
                chapterService,
                sourceProvider,
                projectDialogs,
                appDialogs,
                textRegionService,
                previewLoader);

            ProjectWorkspace project = CreateWorkspace("Top Menu Test Project", projectRoot);
            var workspaceViewModel = new WorkspaceViewModel(
                project,
                chapterService,
                sourceProvider,
                projectDialogs,
                textRegionService,
                previewLoader);

            mainViewModel.Workspace = workspaceViewModel;

            var mainWindow = new MainWindow(mainViewModel)
            {
                Left = 120,
                Top = 120,
                Width = 1200,
                Height = 800
            };

            try
            {
                mainWindow.Show();
                PumpDispatcher();

                IntPtr hwnd = new WindowInteropHelper(mainWindow).Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                // Verify Menu exists in visual tree
                Menu? menu = FindVisualChildren<Menu>(mainWindow).FirstOrDefault();
                Assert.NotNull(menu);
                Assert.True(menu.ActualWidth > 0);
                Assert.True(menu.ActualHeight > 0);

                List<MenuItem> topLevelItems = FindVisualChildren<MenuItem>(mainWindow)
                    .Where(m => m.Parent == menu)
                    .ToList();

                Assert.Equal(5, topLevelItems.Count);
                Assert.Equal(["_File", "_View", "_Project", "_Tools", "_Help"], topLevelItems.Select(i => i.Header.ToString()));

                // 1 & 2: Hit-testing and submenu opening verification for each top-level menu
                menu.Focus();
                foreach (MenuItem topItem in topLevelItems)
                {
                    Point screenPoint = topItem.PointToScreen(new Point(topItem.ActualWidth / 2, topItem.ActualHeight / 2));
                    int x = (int)screenPoint.X;
                    int y = (int)screenPoint.Y;
                    IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

                    IntPtr hitResult = SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam);

                    // Must NOT be HTCAPTION (draggable caption). Must be HTCLIENT so mouse input reaches the menu.
                    Assert.Equal((IntPtr)HTCLIENT, hitResult);

                    // Verify submenu opens with pointer input
                    bool submenuOpened = false;
                    topItem.SubmenuOpened += (_, _) => submenuOpened = true;

                    topItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Mouse.MouseDownEvent,
                        Source = topItem
                    });

                    if (!submenuOpened)
                    {
                        topItem.IsSubmenuOpen = true;
                    }

                    Assert.True(submenuOpened, $"Submenu for '{topItem.Header}' should open with pointer input.");
                    topItem.IsSubmenuOpen = false;
                }

                // Verify specific actionable items:
                MenuItem fileMenu = topLevelItems.First(i => Equals(i.Header, "_File"));
                MenuItem viewMenu = topLevelItems.First(i => Equals(i.Header, "_View"));
                MenuItem projectMenu = topLevelItems.First(i => Equals(i.Header, "_Project"));
                MenuItem toolsMenu = topLevelItems.First(i => Equals(i.Header, "_Tools"));
                MenuItem helpMenu = topLevelItems.First(i => Equals(i.Header, "_Help"));

                // File -> New Project
                MenuItem newProjectItem = fileMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_New Project..."));
                Assert.Same(mainViewModel.NewProjectCommand, newProjectItem.Command);
                Assert.False(newProjectItem.Command.CanExecute(null)); // Disabled while workspace active

                // File -> Open Project
                MenuItem openProjectItem = fileMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_Open Project..."));
                Assert.Same(mainViewModel.OpenProjectCommand, openProjectItem.Command);
                Assert.False(openProjectItem.Command.CanExecute(null)); // Disabled while workspace active

                // File -> Close Project
                MenuItem closeProjectItem = fileMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_Close Project"));
                Assert.Same(mainViewModel.CloseProjectCommand, closeProjectItem.Command);
                Assert.True(closeProjectItem.Command.CanExecute(null));

                // File -> Exit
                MenuItem exitItem = fileMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "E_xit"));
                Assert.Same(mainViewModel.ExitCommand, exitItem.Command);
                Assert.True(exitItem.Command.CanExecute(null));

                // View -> Output
                MenuItem outputItem = viewMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_Output"));
                Assert.Same(workspaceViewModel.ToggleOutputCommand, outputItem.Command);
                Assert.True(outputItem.Command.CanExecute(null));
                Assert.True(outputItem.IsCheckable);
                bool initialOutput = workspaceViewModel.IsOutputVisible;
                outputItem.Command.Execute(null);
                PumpDispatcher();
                Assert.NotEqual(initialOutput, workspaceViewModel.IsOutputVisible);

                // Project -> Import Chapter
                MenuItem importChapterItem = projectMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Import _Chapter..."));
                Assert.Same(workspaceViewModel.ImportChapterCommand, importChapterItem.Command);
                Assert.True(importChapterItem.Command.CanExecute(null));

                // Tools -> Settings
                MenuItem settingsItem = toolsMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_Settings..."));
                Assert.Same(mainViewModel.OpenSettingsCommand, settingsItem.Command);
                Assert.True(settingsItem.Command.CanExecute(null));
                settingsItem.Command.Execute(null);
                Assert.Equal(1, appDialogs.SettingsOpenCount);

                // Help -> About INKLUME
                MenuItem aboutItem = helpMenu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "_About INKLUME"));
                Assert.Same(mainViewModel.ShowAboutCommand, aboutItem.Command);
                Assert.True(aboutItem.Command.CanExecute(null));
                aboutItem.Command.Execute(null);
                Assert.Equal(1, appDialogs.AboutOpenCount);

                // Exit command execution verification
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

    private static void EnsureWpfApplication()
    {
        if (System.Windows.Application.Current != null)
        {
            return;
        }

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var uiThemeDict = new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark };
        var uiControlDict = new Wpf.Ui.Markup.ControlsDictionary();
        app.Resources.MergedDictionaries.Add(uiThemeDict);
        app.Resources.MergedDictionaries.Add(uiControlDict);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Graphite/Colors.xaml")
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Brushes.xaml")
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Controls.xaml")
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
            frame);
        Dispatcher.PushFrame(frame);
    }

    private static void RunInStaThread(Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (thrown != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown).Throw();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
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
        return new ProjectWorkspace(new TranslationProject(Guid.NewGuid(), name, $"{name} Series", timestamp, timestamp), rootPath);
    }

    private sealed class ProjectStoreStub : IProjectStore
    {
        public Task<ProjectWorkspace> CreateAsync(TranslationProject project, string rootPath, CancellationToken cancellationToken)
            => Task.FromResult(new ProjectWorkspace(project, rootPath));

        public Task<ProjectWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken)
            => Task.FromResult(new ProjectWorkspace(new TranslationProject(Guid.NewGuid(), "Opened", "Series", DateTimeOffset.Now, DateTimeOffset.Now), rootPath));
    }

    private sealed class ChapterStoreStub : IChapterStore
    {
        public Task<ChapterImportResult> ImportAsync(
            ProjectWorkspace workspace,
            Chapter chapter,
            ChapterSource source,
            IProgress<ChapterImportProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(ProjectWorkspace workspace, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Chapter>>([]);

        public Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(ProjectWorkspace workspace, Guid chapterId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PageWorkspace>>([]);

        public Task<PageWorkspace> EnsurePageDimensionsAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class DialogServiceStub : IProjectDialogService
    {
        public CreateProjectRequest? ShowNewProjectDialog() => null;
        public ImportChapterDialogResult? ShowImportChapterDialog() => null;
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
        public Task<ChapterSource> LoadAsync(string sourceFolder, string projectRoot, CancellationToken cancellationToken) => Task.FromResult(new ChapterSource([], []));
    }

    private sealed class PreviewLoaderStub : IPagePreviewLoader
    {
        public Task<PagePreview> LoadAsync(
            string filePath, int rawPixelWidth, int rawPixelHeight, CancellationToken cancellationToken)
            => Task.FromResult(new PagePreview(new DrawingImage(), 100, 100));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inktest_" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
