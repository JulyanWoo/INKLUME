using System.IO;
using Inklume.Desktop.Appearance;
using Inklume.Desktop.Settings;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class AppearanceServiceTests
{
    [Fact]
    public void ThemeCatalog_ShouldExposeStableIdentifiers()
    {
        AppearanceTheme[] identifiers = AppearanceThemeCatalog.All
            .Select(definition => definition.Theme)
            .ToArray();

        Assert.Equal(
            [AppearanceTheme.Graphite, AppearanceTheme.Midnight, AppearanceTheme.Amethyst, AppearanceTheme.Paper],
            identifiers);
        Assert.Equal(AppearanceTheme.Graphite, AppearanceThemeCatalog.Default.Theme);
    }

    [Fact]
    public async Task InitializeAsync_ShouldFallBackToGraphite_WhenSettingsJsonIsInvalid()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ invalid json", TestContext.Current.CancellationToken);
        var applier = new RecordingThemeApplier();
        var service = new AppearanceService(new JsonAppSettingsStore(settingsPath), applier);

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearanceTheme.Graphite, service.CurrentTheme.Theme);
        Assert.Equal(AppearanceTheme.Graphite, applier.LastApplied?.Theme);
    }

    [Fact]
    public async Task InitializeAsync_ShouldFallBackToGraphite_WhenThemeIdentifierIsUnknown()
    {
        var store = new SettingsStoreStub
        {
            SettingsToLoad = new AppSettings
            {
                Appearance = new AppearanceSettings { Theme = "UnknownTheme" }
            }
        };
        var service = new AppearanceService(store, new RecordingThemeApplier());

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearanceTheme.Graphite, service.CurrentTheme.Theme);
    }

    [Fact]
    public async Task ChangeThemeAsync_ShouldPersistAndRestoreThemePreference()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "settings.json");
        var firstService = new AppearanceService(
            new JsonAppSettingsStore(settingsPath),
            new RecordingThemeApplier());
        await firstService.InitializeAsync(TestContext.Current.CancellationToken);

        string? error = await firstService.ChangeThemeAsync(
            AppearanceTheme.Midnight,
            TestContext.Current.CancellationToken);
        var restoredService = new AppearanceService(
            new JsonAppSettingsStore(settingsPath),
            new RecordingThemeApplier());
        await restoredService.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.Equal(AppearanceTheme.Midnight, firstService.CurrentTheme.Theme);
        Assert.Equal(AppearanceTheme.Midnight, restoredService.CurrentTheme.Theme);
    }

    [Fact]
    public async Task ChangeThemeAsync_ShouldKeepThemeActive_WhenSettingsCannotBeSaved()
    {
        var store = new SettingsStoreStub { SaveException = new IOException("Storage unavailable.") };
        var applier = new RecordingThemeApplier();
        var service = new AppearanceService(store, applier);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        string? error = await service.ChangeThemeAsync(
            AppearanceTheme.Amethyst,
            TestContext.Current.CancellationToken);

        Assert.NotNull(error);
        Assert.Equal(AppearanceTheme.Amethyst, service.CurrentTheme.Theme);
        Assert.Equal(AppearanceTheme.Amethyst, applier.LastApplied?.Theme);
    }

    private sealed class RecordingThemeApplier : IThemeApplier
    {
        public AppearanceThemeDefinition? LastApplied { get; private set; }

        public void Apply(AppearanceThemeDefinition theme) => LastApplied = theme;
    }

    private sealed class SettingsStoreStub : IAppSettingsStore
    {
        public AppSettings? SettingsToLoad { get; init; }

        public IOException? SaveException { get; init; }

        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult(SettingsToLoad);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
            => SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "appearance-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
