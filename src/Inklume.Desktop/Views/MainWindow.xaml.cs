using System.Windows;
using Inklume.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace Inklume.Desktop.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        viewModel.ExitRequested += OnExitRequested;
    }

    private void OnExitRequested(object? sender, EventArgs e) => Close();
}
