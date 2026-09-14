using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Desktop.Appearance;

namespace Inklume.Desktop.ViewModels;

public sealed partial class SettingsDialogViewModel : ObservableObject
{
    private readonly AppearanceService _appearanceService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectThemeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public SettingsDialogViewModel(AppearanceService appearanceService)
    {
        ArgumentNullException.ThrowIfNull(appearanceService);
        _appearanceService = appearanceService;
        Themes = new ObservableCollection<ThemeOptionViewModel>(
            appearanceService.AvailableThemes.Select(definition => new ThemeOptionViewModel(
                definition,
                definition.Theme == appearanceService.CurrentTheme.Theme)));
        SelectThemeCommand = new AsyncRelayCommand<ThemeOptionViewModel>(SelectThemeAsync, CanSelectTheme);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<ThemeOptionViewModel> Themes { get; }

    public IAsyncRelayCommand<ThemeOptionViewModel> SelectThemeCommand { get; }

    public IRelayCommand CloseCommand { get; }

    private bool CanSelectTheme(ThemeOptionViewModel? theme) => theme is not null && !IsBusy;

    private async Task SelectThemeAsync(ThemeOptionViewModel? theme)
    {
        if (theme is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            ErrorMessage = await _appearanceService.ChangeThemeAsync(theme.Theme, CancellationToken.None)
                ?? string.Empty;
            foreach (ThemeOptionViewModel option in Themes)
            {
                option.IsSelected = option.Theme == _appearanceService.CurrentTheme.Theme;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
