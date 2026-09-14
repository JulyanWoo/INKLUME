using System.Collections.ObjectModel;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Inklume.Desktop.Appearance;

public sealed record AppearanceThemeDefinition(
    AppearanceTheme Theme,
    string Name,
    string Description,
    ApplicationTheme WpfTheme,
    string PaletteResourceUri,
    Color AccentColor,
    Color PreviewBackground,
    Color PreviewPanel);

public static class AppearanceThemeCatalog
{
    private static readonly ReadOnlyCollection<AppearanceThemeDefinition> Themes = Array.AsReadOnly<AppearanceThemeDefinition>(
    [
        new(
            AppearanceTheme.Graphite,
            "INKLUME Graphite",
            "Neutral charcoal with a restrained cobalt blue accent.",
            ApplicationTheme.Dark,
            "pack://application:,,,/Inklume.Desktop;component/Themes/Graphite/Colors.xaml",
            Color.FromRgb(0x3B, 0x82, 0xF6),
            Color.FromRgb(0x16, 0x19, 0x1E),
            Color.FromRgb(0x21, 0x26, 0x2E)),
        new(
            AppearanceTheme.Midnight,
            "Midnight",
            "Deep oceanic navy surfaces with a luminous cyan accent.",
            ApplicationTheme.Dark,
            "pack://application:,,,/Inklume.Desktop;component/Themes/Midnight/Colors.xaml",
            Color.FromRgb(0x03, 0x69, 0xA1),
            Color.FromRgb(0x0C, 0x13, 0x26),
            Color.FromRgb(0x16, 0x25, 0x4A)),
        new(
            AppearanceTheme.Amethyst,
            "Amethyst",
            "Deep obsidian plum surfaces with a vivid royal violet accent.",
            ApplicationTheme.Dark,
            "pack://application:,,,/Inklume.Desktop;component/Themes/Amethyst/Colors.xaml",
            Color.FromRgb(0xB2, 0x5B, 0xFF),
            Color.FromRgb(0x1A, 0x10, 0x26),
            Color.FromRgb(0x30, 0x1D, 0x47)),
        new(
            AppearanceTheme.Paper,
            "Paper",
            "Warm off-white surfaces with dark ink and classic blue accent.",
            ApplicationTheme.Light,
            "pack://application:,,,/Inklume.Desktop;component/Themes/Paper/Colors.xaml",
            Color.FromRgb(0x09, 0x69, 0xDA),
            Color.FromRgb(0xF3, 0xF0, 0xEA),
            Color.FromRgb(0xFF, 0xFF, 0xFF))
    ]);

    public static IReadOnlyList<AppearanceThemeDefinition> All => Themes;

    public static AppearanceThemeDefinition Default => Get(AppearanceTheme.Graphite);

    public static AppearanceThemeDefinition Get(AppearanceTheme theme)
        => Themes.First(definition => definition.Theme == theme);

    public static AppearanceThemeDefinition? Find(string? identifier)
        => Enum.TryParse(identifier, ignoreCase: true, out AppearanceTheme theme)
            ? Themes.FirstOrDefault(definition => definition.Theme == theme)
            : null;
}
