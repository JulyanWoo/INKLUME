using System.Windows;
using Inklume.Application.Projects;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;

namespace Inklume.Desktop.Services;

public sealed class DesktopProjectDialogService(IProjectFolderPicker folderPicker) : IProjectDialogService
{
    public CreateProjectRequest? ShowNewProjectDialog()
    {
        var viewModel = new NewProjectDialogViewModel(folderPicker);
        var dialog = new NewProjectDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    public ImportChapterDialogResult? ShowImportChapterDialog()
    {
        var viewModel = new ImportChapterDialogViewModel(folderPicker);
        var dialog = new ImportChapterDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    public string? PickProjectFolder() => folderPicker.PickFolder("Open an INKLUME project folder");
}
