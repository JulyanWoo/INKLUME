using System.ComponentModel;
using System.IO;
using System.Windows;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Appearance;
using Inklume.Desktop.Services;
using Inklume.Desktop.Settings;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.TextRegions;

namespace Inklume.Desktop;

public partial class App : System.Windows.Application
{
    private MainViewModel? _mainViewModel;
    private bool _isClosing;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Inklume",
            "settings.json");
        var appearanceService = new AppearanceService(
            new JsonAppSettingsStore(settingsPath),
            new WpfThemeApplier(Resources));
        await appearanceService.InitializeAsync(CancellationToken.None);

        var projectService = new ProjectService(new FileSystemProjectStore(), TimeProvider.System);
        var chapterService = new ChapterService(new FileSystemChapterStore(), TimeProvider.System);
        var textRegionService = new TextRegionService(new SqliteTextRegionStore(), TimeProvider.System);
        var folderPicker = new WindowsProjectFolderPicker();
        var applicationDialogService = new DesktopApplicationDialogService(appearanceService);
        _mainViewModel = new MainViewModel(
            projectService,
            chapterService,
            new LocalFolderChapterSourceProvider(),
            new DesktopProjectDialogService(folderPicker),
            applicationDialogService,
            textRegionService,
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
