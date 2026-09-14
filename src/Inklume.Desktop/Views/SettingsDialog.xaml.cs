using Inklume.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace Inklume.Desktop.Views;

public partial class SettingsDialog : FluentWindow
{
    private readonly SettingsDialogViewModel _viewModel;

    public SettingsDialog(SettingsDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
