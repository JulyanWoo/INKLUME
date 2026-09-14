namespace Inklume.Desktop.Settings;

public interface IAppSettingsStore
{
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
