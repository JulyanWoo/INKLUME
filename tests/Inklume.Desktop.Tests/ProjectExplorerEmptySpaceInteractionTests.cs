using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Desktop.Tests;

[Collection("WpfRenderCollection")]
public sealed class ProjectExplorerEmptySpaceInteractionTests
{
    [Fact]
    public void ProjectExplorer_EmptySpacePointerInteractions_MustBeInert_AndPreserveSelectionAndExpansion()
    {
        WpfTestRunner.Run(() =>
        {

            using var tempDir = new TemporaryDirectory();
            string sourceRoot = Path.Combine(tempDir.Path, "ExplorerTestProject");
            string dataRoot = Path.Combine(tempDir.Path, "ExplorerTestData");
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(dataRoot);

            string chapterDir = Path.Combine(sourceRoot, "chapters", "001");
            string rawDir = Path.Combine(chapterDir, "001_raw");
            Directory.CreateDirectory(rawDir);
            string page1Path = Path.Combine(rawDir, "001.png");
            string page2Path = Path.Combine(rawDir, "002.png");
            File.WriteAllBytes(page1Path, [137, 80, 78, 71, 13, 10, 26, 10]);
            File.WriteAllBytes(page2Path, [137, 80, 78, 71, 13, 10, 26, 10]);

            var project = new TranslationProject(Guid.NewGuid(), "Explorer Test", "Explorer Series", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var workspace = new ProjectWorkspace(project, sourceRoot, dataRoot);

            var chapter1 = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(1), "Chapter 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var page1 = new PageWorkspace(
                new Inklume.Domain.Projects.Page(Guid.NewGuid(), chapter1.Id, 1, "001.png", Path.GetRelativePath(sourceRoot, page1Path), new string('a', 64), 100, 100),
                page1Path);
            var page2 = new PageWorkspace(
                new Inklume.Domain.Projects.Page(Guid.NewGuid(), chapter1.Id, 2, "002.png", Path.GetRelativePath(sourceRoot, page2Path), new string('b', 64), 100, 100),
                page2Path);

            string chapter2Dir = Path.Combine(sourceRoot, "002");
            Directory.CreateDirectory(chapter2Dir);
            string page3Path = Path.Combine(chapter2Dir, "003.png");
            File.WriteAllBytes(page3Path, [137, 80, 78, 71, 13, 10, 26, 10]);
            var chapter2 = new Chapter(Guid.NewGuid(), project.Id, new ChapterNumber(2), "Chapter 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var page3 = new PageWorkspace(
                new Inklume.Domain.Projects.Page(Guid.NewGuid(), chapter2.Id, 1, "003.png", Path.GetRelativePath(sourceRoot, page3Path), new string('c', 64), 100, 100),
                page3Path);

            var chapterStore = new ChapterStoreStub([chapter1, chapter2], [page1, page2, page3]);
            var chapterService = new ChapterService(chapterStore, TimeProvider.System);
            var sourceProvider = new EmptySourceProvider();
            var dialogService = new DialogServiceStub();
            var textRegionStore = new TextRegionStoreStub();
            textRegionStore.AddPage(page1.Page);
            textRegionStore.AddPage(page2.Page);
            textRegionStore.AddPage(page3.Page);
            var textRegionService = new TextRegionService(textRegionStore, TimeProvider.System);
            var previewLoader = new PreviewLoaderStub();

            var workspaceViewModel = new WorkspaceViewModel(
                workspace,
                chapterService,
                sourceProvider,
                dialogService,
                textRegionService,
                previewLoader);

            Await(workspaceViewModel.InitializeAsync(CancellationToken.None));

            var workspaceView = new WorkspaceView
            {
                DataContext = workspaceViewModel,
                Width = 800,
                Height = 600
            };

            var window = new Window
            {
                Content = workspaceView,
                Width = 1000,
                Height = 700,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            try
            {
                window.Show();
                PumpDispatcher();

                TreeView? treeView = FindVisualChildByName<TreeView>(workspaceView, "ProjectExplorer");
                Assert.NotNull(treeView);

                // Load tree items: expand root, chapters folder, and chapter 1
                ProjectExplorerNode rootNode = Assert.Single(workspaceViewModel.ExplorerRoots);
                rootNode.IsExpanded = true;
                PumpDispatcher();

                ProjectExplorerNode chaptersFolderNode = rootNode.Children.First(c => c.Kind == ExplorerNodeKind.ChaptersFolder);
                Await(chaptersFolderNode.EnsureLoadedAsync());
                chaptersFolderNode.IsExpanded = true;
                PumpDispatcher();

                ProjectExplorerNode chapter1Node = chaptersFolderNode.Children.First(c => c.DisplayName == "Chapter 1");
                ProjectExplorerNode chapter2Node = chaptersFolderNode.Children.First(c => c.DisplayName == "Chapter 2");
                Await(chapter1Node.EnsureLoadedAsync());
                chapter1Node.IsExpanded = true;
                PumpDispatcher();

                ProjectExplorerNode rawFolderNode = chapter1Node.Children.First(c => c.Kind == ExplorerNodeKind.RawFolder);
                Await(rawFolderNode.EnsureLoadedAsync());
                rawFolderNode.IsExpanded = true;
                PumpDispatcher();

                ProjectExplorerNode page1Node = rawFolderNode.Children.First(c => c.DisplayName == "001.png");
                ProjectExplorerNode page2Node = rawFolderNode.Children.First(c => c.DisplayName == "002.png");

                // 1. Select a Page/Image node
                Await(workspaceViewModel.SelectExplorerNodeAsync(page1Node));
                PumpDispatcher();

                Assert.Same(page1Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(workspaceViewModel.HasSelectedPage);
                Assert.Equal(page1.Page.Id, workspaceViewModel.SelectedPage?.Page.Id);
                Assert.Equal(page1.Page.Id, workspaceViewModel.VisualEditor.SelectedPage?.Page.Id);

                // 2. Click empty Explorer background
                // Hit-test empty space at the bottom of the TreeView
                Point emptyPoint = new(treeView.ActualWidth / 2, treeView.ActualHeight - 15);
                IInputElement? hitElement = treeView.InputHitTest(emptyPoint);
                Assert.NotNull(hitElement);
                Assert.True(hitElement is DependencyObject);

                var emptyClickArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseDownEvent,
                    Source = hitElement
                };
                ((UIElement)hitElement).RaiseEvent(emptyClickArgs);
                PumpDispatcher();

                // 3. Selected node remains unchanged
                Assert.True(emptyClickArgs.Handled, "Click on empty background must be marked Handled.");
                Assert.Same(page1Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(page1Node.IsSelected);
                Assert.False(rootNode.IsSelected);

                // 4. Visual Editor remains unchanged
                Assert.Equal(page1.Page.Id, workspaceViewModel.VisualEditor.SelectedPage?.Page.Id);

                // 5. Root expansion state remains unchanged
                Assert.True(rootNode.IsExpanded);

                // 6. Double-click empty Explorer background
                var emptyDoubleClickPreviewArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.PreviewMouseDoubleClickEvent,
                    Source = hitElement
                };
                treeView.RaiseEvent(emptyDoubleClickPreviewArgs);
                PumpDispatcher();

                // 7. Root expansion state remains unchanged
                Assert.True(emptyDoubleClickPreviewArgs.Handled, "Double-click on empty background must be marked Handled.");
                Assert.True(rootNode.IsExpanded);

                // 8. Child folder expansion states remain unchanged
                Assert.True(chaptersFolderNode.IsExpanded);
                Assert.True(chapter1Node.IsExpanded);
                Assert.False(chapter2Node.IsExpanded);

                // 9. Selection remains unchanged
                Assert.Same(page1Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(page1Node.IsSelected);
                Assert.False(rootNode.IsSelected);

                // 10. Click folder disclosure arrow (Expander on chapter2)
                TreeViewItem? chapter2Item = GetTreeViewItemForNode(treeView, chapter2Node);
                Assert.NotNull(chapter2Item);
                ToggleButton? chapter2Expander = FindVisualChildByName<ToggleButton>(chapter2Item, "Expander");
                Assert.NotNull(chapter2Expander);

                // Verify disclosure arrow click toggles expansion
                chapter2Expander.IsChecked = true; // toggled
                PumpDispatcher();

                // 11. Folder expands/collapses normally
                Assert.True(chapter2Node.IsExpanded);

                // 12. Selection behavior remains consistent: page1 remains selected (Expanded != Selected)
                Assert.Same(page1Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(page1Node.IsSelected);
                Assert.False(chapter2Node.IsSelected);

                // Expand back / collapse
                chapter2Expander.IsChecked = false;
                PumpDispatcher();
                Assert.False(chapter2Node.IsExpanded);
                Assert.Same(page1Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(page1Node.IsSelected);
                Assert.False(chapter2Node.IsSelected);

                // 13. Click another actual node (page2Node)
                TreeViewItem? page2Item = GetTreeViewItemForNode(treeView, page2Node);
                Assert.NotNull(page2Item);
                FrameworkElement? page2Header = FindVisualChildByName<FrameworkElement>(page2Item, "PART_Header");
                Assert.NotNull(page2Header);

                var page2ClickArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseDownEvent,
                    Source = page2Header
                };
                page2Header.RaiseEvent(page2ClickArgs);
                PumpDispatcher();

                // Node click was NOT suppressed
                Assert.False(page2ClickArgs.Handled, "Click on actual node must NOT be marked Handled.");

                // 14. Selection changes normally
                Await(workspaceViewModel.SelectExplorerNodeAsync(page2Node));
                PumpDispatcher();

                Assert.Same(page2Node, workspaceViewModel.SelectedExplorerNode);
                Assert.True(page2Node.IsSelected);
                Assert.False(page1Node.IsSelected);
                Assert.Equal(page2.Page.Id, workspaceViewModel.SelectedPage?.Page.Id);
                Assert.Equal(page2.Page.Id, workspaceViewModel.VisualEditor.SelectedPage?.Page.Id);
            }
            finally
            {
                window.Close();
                PumpDispatcher();
            }
        });
    }

    private static TreeViewItem? GetTreeViewItemForNode(ItemsControl parent, object dataItem)
    {
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(dataItem) is TreeViewItem item)
        {
            return item;
        }

        for (int i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is TreeViewItem childItem)
            {
                childItem.ApplyTemplate();
                var subItem = GetTreeViewItemForNode(childItem, dataItem);
                if (subItem != null)
                {
                    return subItem;
                }
            }
        }

        return null;
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
            {
                return typed;
            }

            T? found = FindVisualChildByName<T>(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void Await(Task task)
    {
        if (task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
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
