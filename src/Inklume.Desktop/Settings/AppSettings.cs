namespace Inklume.Desktop.Settings;

public sealed record AppSettings
{
    public AppearanceSettings Appearance { get; init; } = new();
}

public sealed record AppearanceSettings
{
    public string Theme { get; init; } = nameof(Inklume.Desktop.Appearance.AppearanceTheme.Graphite);
}
