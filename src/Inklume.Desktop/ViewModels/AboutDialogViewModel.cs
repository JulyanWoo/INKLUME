using CommunityToolkit.Mvvm.Input;

namespace Inklume.Desktop.ViewModels;

public sealed class AboutDialogViewModel
{
    public AboutDialogViewModel(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Version = version;
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    public string ApplicationName { get; } = "INKLUME";

    public string Subtitle { get; } = "AI Comic Localization Studio";

    public string Version { get; }

    public IRelayCommand CloseCommand { get; }
}
