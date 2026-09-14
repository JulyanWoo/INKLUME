using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Inklume.Desktop.Appearance;

namespace Inklume.Desktop.ViewModels;

public sealed partial class ThemeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ThemeOptionViewModel(AppearanceThemeDefinition definition, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Theme = definition.Theme;
        Name = definition.Name;
        Description = definition.Description;
        PreviewBackground = CreateBrush(definition.PreviewBackground);
        PreviewPanel = CreateBrush(definition.PreviewPanel);
        PreviewAccent = CreateBrush(definition.AccentColor);
        _isSelected = isSelected;
    }

    public AppearanceTheme Theme { get; }

    public string Name { get; }

    public string Description { get; }

    public Brush PreviewBackground { get; }

    public Brush PreviewPanel { get; }

    public Brush PreviewAccent { get; }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
