using Inklume.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace Inklume.Desktop.Views;

public partial class AboutDialog : FluentWindow
{
    private readonly AboutDialogViewModel _viewModel;

    public AboutDialog(AboutDialogViewModel viewModel)
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
