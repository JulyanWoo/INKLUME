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
using Inklume.Imaging;
using Inklume.Imaging.Worker;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.TextRegions;

namespace Inklume.Desktop;

public partial class App : System.Windows.Application
{
    private MainViewModel? _mainViewModel;
    private OcrWorkerClient? _workerClient;
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

        string? pythonPath = OcrRuntimeLocator.FindPythonPath();
        string? workerScriptPath = OcrRuntimeLocator.FindWorkerScriptPath();
        OcrService? ocrService = null;
        if (pythonPath is not null && workerScriptPath is not null)
        {
            var transport = new ProcessOcrWorkerTransport(pythonPath, workerScriptPath);
            _workerClient = new OcrWorkerClient(transport);
            var ocrProvider = new PaddleOcrProvider(_workerClient);
            var ocrStore = new SqliteOcrStore();
            var regionStore = new SqliteTextRegionStore();
            ocrService = new OcrService(ocrProvider, ocrStore, regionStore);
        }

        var reviewService = new TextRegionReviewService(
            new SqliteTextRegionStore(), new SqliteOcrStore(), TimeProvider.System);

        _mainViewModel = new MainViewModel(
            projectService,
            chapterService,
            new LocalFolderChapterSourceProvider(),
            new DesktopProjectDialogService(folderPicker),
            applicationDialogService,
            textRegionService,
            new WpfPagePreviewLoader(),
            ocrService,
            reviewService);
        MainWindow = new MainWindow(_mainViewModel);
        MainWindow.Closing += OnMainWindowClosing;
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_workerClient is not null)
        {
            _workerClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
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
