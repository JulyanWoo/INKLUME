using Microsoft.Win32;

namespace Inklume.Desktop.Services;

public sealed class WindowsProjectFolderPicker : IProjectFolderPicker
{
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            AddToRecent = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
