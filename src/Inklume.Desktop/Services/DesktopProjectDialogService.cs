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

    public ImportChapterDialogResult? ShowImportChapterDialog(string? initialSourceFolder = null)
    {
        var viewModel = new ImportChapterDialogViewModel(folderPicker, initialSourceFolder);
        var dialog = new ImportChapterDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    public string? PickProjectFolder() => folderPicker.PickFolder("Select a comic or series folder");
}
