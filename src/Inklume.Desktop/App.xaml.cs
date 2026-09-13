using System.ComponentModel;
using System.Windows;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Desktop.Services;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;

namespace Inklume.Desktop;

public partial class App : System.Windows.Application
{
    private MainViewModel? _mainViewModel;
    private bool _isClosing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var projectService = new ProjectService(new FileSystemProjectStore(), TimeProvider.System);
        var chapterService = new ChapterService(new FileSystemChapterStore(), TimeProvider.System);
        _mainViewModel = new MainViewModel(
            projectService,
            chapterService,
            new LocalFolderChapterSourceProvider(),
            new WindowsProjectFolderPicker(),
            new WpfPagePreviewLoader());
        MainWindow = new MainWindow(_mainViewModel);
        MainWindow.Closing += OnMainWindowClosing;
        MainWindow.Show();
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            e.Cancel = true;
            return;
        }

        if (_mainViewModel is not { IsBusy: true } viewModel || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        await viewModel.StopAsync();
        window.Closing -= OnMainWindowClosing;
        window.Close();
    }
}
