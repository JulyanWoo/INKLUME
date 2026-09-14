using System.IO;
using System.Text.Json;

namespace Inklume.Desktop.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private const long MaximumSettingsFileSize = 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public JsonAppSettingsStore(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = Path.GetFullPath(settingsFilePath);
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            _settingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumSettingsFileSize)
        {
            throw new InvalidDataException("The application settings file is larger than the supported limit.");
        }

        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("The application settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
