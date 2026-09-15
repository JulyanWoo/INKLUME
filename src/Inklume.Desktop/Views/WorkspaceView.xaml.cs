using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Inklume.Desktop.ViewModels;
using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private bool _isClassificationUpdatePending;

    public WorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnExplorerSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WorkspaceViewModel viewModel)
        {
            await viewModel.SelectExplorerNodeAsync(e.NewValue as ProjectExplorerNode);
        }
    }

    private void OnProjectExplorerPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left or MouseButton.Right
            && !IsVisualInNodeRow(e.OriginalSource as DependencyObject, ProjectExplorer))
        {
            e.Handled = true;
        }
    }

    private void OnProjectExplorerPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsVisualInNodeRow(e.OriginalSource as DependencyObject, ProjectExplorer))
        {
            e.Handled = true;
        }
    }

    private static bool IsVisualInNodeRow(DependencyObject? source, TreeView treeView)
    {
        DependencyObject? current = source;
        while (current != null && current != treeView)
        {
            if (current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }

            if (current is FrameworkElement fe)
            {
                if (fe.Name is "ItemRow" or "PART_Header" or "Expander")
                {
                    return true;
                }

                if (fe.Name == "ItemsHost" || fe is ItemsPresenter)
                {
                    return false;
                }
            }

            if (current is TreeViewItem)
            {
                return false;
            }

            current = GetVisualParent(current);
        }

        return false;
    }

    private static DependencyObject? GetVisualParent(DependencyObject element)
    {
        if (element is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(element);
        }

        if (element is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        return null;
    }

    private async void OnRegionClassificationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isClassificationUpdatePending
            || sender is not ComboBox { IsKeyboardFocusWithin: true }
            || !IsLoaded || DataContext is not WorkspaceViewModel workspace
            || workspace.VisualEditor.SelectedRegion is not { } region
            || RegionRoleSelector.SelectedItem is not TextRegionRole role
            || RegionContainerSelector.SelectedItem is not TextContainerType containerType
            || (region.Role == role && region.ContainerType == containerType))
        {
            return;
        }

        try
        {
            _isClassificationUpdatePending = true;
            await workspace.VisualEditor.UpdateSelectedClassificationAsync(role, containerType);
        }
        catch (Exception exception)
        {
            workspace.VisualEditor.ReportInteractionError(exception);
        }
        finally
        {
            _isClassificationUpdatePending = false;
        }
    }
}
