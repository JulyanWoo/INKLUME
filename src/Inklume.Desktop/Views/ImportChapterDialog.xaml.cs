using System.Windows;
using Inklume.Desktop.ViewModels;

namespace Inklume.Desktop.Views;

public partial class ImportChapterDialog : Window
{
    private readonly ImportChapterDialogViewModel _viewModel;

    public ImportChapterDialog(ImportChapterDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object? sender, DialogCloseRequestedEventArgs e) => DialogResult = e.Accepted;

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Closed -= OnClosed;
    }
}
