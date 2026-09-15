using Inklume.Application.Projects;

namespace Inklume.Desktop.Services;

public interface IProjectDialogService
{
    CreateProjectRequest? ShowNewProjectDialog();

    ImportChapterDialogResult? ShowImportChapterDialog(string? initialSourceFolder = null);

    string? PickProjectFolder();
}
