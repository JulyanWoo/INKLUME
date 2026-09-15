using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Desktop.Services;
using Inklume.Domain.Projects;

namespace Inklume.Desktop.ViewModels;

public sealed partial class ImportChapterDialogViewModel : ObservableObject
{
    private readonly IProjectFolderPicker _folderPicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _chapterNumber = string.Empty;

    [ObservableProperty]
    private string _chapterTitle = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _sourceFolder = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ImportChapterDialogViewModel(IProjectFolderPicker folderPicker, string? initialSourceFolder = null)
    {
        ArgumentNullException.ThrowIfNull(folderPicker);
        _folderPicker = folderPicker;
        if (!string.IsNullOrWhiteSpace(initialSourceFolder))
        {
            _sourceFolder = initialSourceFolder;
        }

        BrowseCommand = new RelayCommand(Browse);
        ImportCommand = new RelayCommand(Import, CanImport);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(false)));
    }

    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    public ImportChapterDialogResult? Result { get; private set; }

    public IRelayCommand BrowseCommand { get; }

    public IRelayCommand ImportCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private bool CanImport() => !string.IsNullOrWhiteSpace(ChapterNumber)
        && !string.IsNullOrWhiteSpace(SourceFolder);

    private void Browse()
    {
        try
        {
            string? selectedFolder = _folderPicker.PickFolder("Choose the folder containing the chapter images");
            if (selectedFolder is not null)
            {
                SourceFolder = selectedFolder;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Chapter source folder selection failed: {0}", exception);
            ErrorMessage = "The folder picker could not be opened. Please try again.";
        }
    }

    private void Import()
    {
        if (!TryReadChapterNumber(out ChapterNumber number))
        {
            ErrorMessage = "Enter a non-negative chapter number, such as 1 or 10.5.";
            return;
        }

        string? title = string.IsNullOrWhiteSpace(ChapterTitle) ? null : ChapterTitle;
        Result = new ImportChapterDialogResult(number, title, SourceFolder);
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }

    private bool TryReadChapterNumber(out ChapterNumber number)
    {
        if (Inklume.Domain.Projects.ChapterNumber.TryParse(ChapterNumber, out number))
        {
            return true;
        }

        const NumberStyles styles = NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowLeadingWhite
            | NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(ChapterNumber, styles, CultureInfo.CurrentCulture, out decimal parsed) && parsed >= 0)
        {
            number = new ChapterNumber(parsed);
            return true;
        }

        number = default;
        return false;
    }
}
