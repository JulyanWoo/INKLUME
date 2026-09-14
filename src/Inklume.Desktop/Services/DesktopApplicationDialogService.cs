using System.Reflection;
using Inklume.Desktop.Appearance;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.Views;

namespace Inklume.Desktop.Services;

public sealed class DesktopApplicationDialogService(AppearanceService appearanceService) : IApplicationDialogService
{
    public void ShowSettingsDialog()
    {
        var viewModel = new SettingsDialogViewModel(appearanceService);
        var dialog = new SettingsDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    public void ShowAboutDialog()
    {
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "Unknown";
        var dialog = new AboutDialog(new AboutDialogViewModel(version))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
