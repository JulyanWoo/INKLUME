using System.Windows;
using System.Windows.Controls;
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
