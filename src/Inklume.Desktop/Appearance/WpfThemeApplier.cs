using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace Inklume.Desktop.Appearance;

public sealed class WpfThemeApplier : IThemeApplier
{
    private readonly ResourceDictionary _applicationResources;
    private readonly ThemesDictionary _wpfThemeDictionary;
    private readonly ResourceDictionary _paletteDictionary;

    public WpfThemeApplier(ResourceDictionary applicationResources)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);
        _applicationResources = applicationResources;

        _wpfThemeDictionary = applicationResources.MergedDictionaries
            .OfType<ThemesDictionary>()
            .Single();
        _paletteDictionary = applicationResources.MergedDictionaries
            .Single(dictionary => dictionary.Source?.OriginalString.Contains(
                "Colors.xaml",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    public void Apply(AppearanceThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var targetUri = new Uri(theme.PaletteResourceUri, UriKind.Absolute);
        var newDictionary = new ResourceDictionary { Source = targetUri };

        _paletteDictionary.Source = targetUri;
        _wpfThemeDictionary.Theme = theme.WpfTheme;

        ApplicationThemeManager.Apply(theme.WpfTheme, WindowBackdropType.None, false);
        ApplicationAccentColorManager.Apply(
            theme.AccentColor,
            theme.WpfTheme,
            false,
            false);

        foreach (var key in newDictionary.Keys)
        {
            _applicationResources[key] = newDictionary[key];
        }

        _applicationResources["SystemAccentColor"] = theme.AccentColor;
        _applicationResources["SystemAccentBrush"] = new SolidColorBrush(theme.AccentColor);
        _applicationResources["AccentButtonBackground"] = new SolidColorBrush(theme.AccentColor);
        _applicationResources["ControlElevationAccentBrush"] = new SolidColorBrush(theme.AccentColor);

        if (System.Windows.Application.Current is { } app)
        {
            foreach (Window window in app.Windows)
            {
                if (window is FluentWindow fluentWindow)
                {
                    fluentWindow.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
                }
            }
        }
    }
}
