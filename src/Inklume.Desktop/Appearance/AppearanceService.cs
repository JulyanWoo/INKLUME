using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Inklume.Desktop.Settings;

namespace Inklume.Desktop.Appearance;

public sealed class AppearanceService(IAppSettingsStore settingsStore, IThemeApplier themeApplier)
{
    public AppearanceThemeDefinition CurrentTheme { get; private set; } = AppearanceThemeCatalog.Default;

    public IReadOnlyList<AppearanceThemeDefinition> AvailableThemes => AppearanceThemeCatalog.All;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppearanceThemeDefinition theme = AppearanceThemeCatalog.Default;
        try
        {
            AppSettings? settings = await settingsStore.LoadAsync(cancellationToken);
            theme = AppearanceThemeCatalog.Find(settings?.Appearance?.Theme) ?? AppearanceThemeCatalog.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Application appearance settings could not be loaded: {0}", exception);
        }

        Apply(theme);
    }

    public async Task<string?> ChangeThemeAsync(AppearanceTheme theme, CancellationToken cancellationToken)
    {
        AppearanceThemeDefinition definition = AppearanceThemeCatalog.Get(theme);
        Apply(definition);

        try
        {
            var settings = new AppSettings
            {
                Appearance = new AppearanceSettings { Theme = theme.ToString() }
            };
            await settingsStore.SaveAsync(settings, cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError("Application appearance settings could not be saved: {0}", exception);
            return "The theme is active for this session, but the preference could not be saved.";
        }
    }

    private void Apply(AppearanceThemeDefinition theme)
    {
        themeApplier.Apply(theme);
        CurrentTheme = theme;
    }
}
