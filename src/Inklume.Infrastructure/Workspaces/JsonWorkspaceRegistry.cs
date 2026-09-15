using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inklume.Application.Workspaces;

namespace Inklume.Infrastructure.Workspaces;

public sealed class JsonWorkspaceRegistry : IWorkspaceRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonWorkspaceRegistry(IWorkspaceDataLocation dataLocation)
    {
        ArgumentNullException.ThrowIfNull(dataLocation);
        _dataLocation = dataLocation;
    }

    public async Task<WorkspaceRegistryEntry?> FindBySourceRootAsync(
        string sourceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        string normalizedSource = NormalizePath(sourceRoot);
        RegistryDocument document = await LoadDocumentAsync(cancellationToken);
        return document.Workspaces.FirstOrDefault(
            entry => string.Equals(NormalizePath(entry.SourceRoot), normalizedSource, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<WorkspaceRegistryEntry?> FindByIdAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return null;
        }

        RegistryDocument document = await LoadDocumentAsync(cancellationToken);
        return document.Workspaces.FirstOrDefault(entry => entry.ProjectId == projectId);
    }

    public async Task<IReadOnlyList<WorkspaceRegistryEntry>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        RegistryDocument document = await LoadDocumentAsync(cancellationToken);
        return [.. document.Workspaces.OrderByDescending(entry => entry.LastOpenedAt)];
    }

    public async Task RegisterOrUpdateAsync(
        WorkspaceRegistryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            RegistryDocument document = await ReadDocumentUnsynchronizedAsync(cancellationToken);
            string normalizedSource = NormalizePath(entry.SourceRoot);
            var updatedList = document.Workspaces
                .Where(existing => existing.ProjectId != entry.ProjectId
                    && !string.Equals(NormalizePath(existing.SourceRoot), normalizedSource, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var normalizedEntry = new WorkspaceRegistryEntry(
                entry.ProjectId,
                entry.ProjectName,
                entry.SeriesName,
                normalizedSource,
                NormalizePath(entry.DataRoot),
                entry.CreatedAt,
                entry.UpdatedAt,
                entry.LastOpenedAt,
                entry.IsLegacyMigrated);

            updatedList.Add(normalizedEntry);
            document = document with { Workspaces = updatedList };
            await WriteDocumentUnsynchronizedAsync(document, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RelocateSourceAsync(
        Guid projectId, string newSourceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSourceRoot);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            RegistryDocument document = await ReadDocumentUnsynchronizedAsync(cancellationToken);
            string normalizedNewSource = NormalizePath(newSourceRoot);
            int index = document.Workspaces.FindIndex(entry => entry.ProjectId == projectId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Workspace with ID '{projectId}' was not found in the registry.");
            }

            WorkspaceRegistryEntry existing = document.Workspaces[index];
            var updated = existing with
            {
                SourceRoot = normalizedNewSource,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var updatedList = document.Workspaces
                .Where(e => e.ProjectId != projectId
                    && !string.Equals(NormalizePath(e.SourceRoot), normalizedNewSource, StringComparison.OrdinalIgnoreCase))
                .ToList();

            updatedList.Add(updated);
            document = document with { Workspaces = updatedList };
            await WriteDocumentUnsynchronizedAsync(document, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            RegistryDocument document = await ReadDocumentUnsynchronizedAsync(cancellationToken);
            var updatedList = document.Workspaces.Where(entry => entry.ProjectId != projectId).ToList();
            if (updatedList.Count != document.Workspaces.Count)
            {
                document = document with { Workspaces = updatedList };
                await WriteDocumentUnsynchronizedAsync(document, cancellationToken);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    internal static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private async Task<RegistryDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadDocumentUnsynchronizedAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<RegistryDocument> ReadDocumentUnsynchronizedAsync(CancellationToken cancellationToken)
    {
        string filePath = _dataLocation.RegistryFilePath;
        if (!File.Exists(filePath))
        {
            return new RegistryDocument(1, []);
        }

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            RegistryDocument? document = await JsonSerializer.DeserializeAsync<RegistryDocument>(
                stream, JsonOptions, cancellationToken);
            return document ?? new RegistryDocument(1, []);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            Trace.TraceWarning("Corrupted workspace registry file at '{0}'. Backing up and attempting reconstruction.", filePath);
            BackupCorruptedRegistry(filePath);
            RegistryDocument reconstructed = AttemptReconstructFromWorkspaceDescriptors();
            try
            {
                await WriteDocumentUnsynchronizedAsync(reconstructed, cancellationToken);
            }
            catch
            {
                // Best effort write of reconstructed document
            }
            return reconstructed;
        }
    }

    private RegistryDocument AttemptReconstructFromWorkspaceDescriptors()
    {
        var entries = new List<WorkspaceRegistryEntry>();
        string workspacesDir = _dataLocation.WorkspacesRoot;
        if (!Directory.Exists(workspacesDir))
        {
            return new RegistryDocument(1, entries);
        }

        try
        {
            foreach (string subDir in Directory.EnumerateDirectories(workspacesDir))
            {
                string descriptorPath = Path.Combine(subDir, "workspace.json");
                if (!File.Exists(descriptorPath))
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(descriptorPath);
                    WorkspaceDescriptor? descriptor = JsonSerializer.Deserialize<WorkspaceDescriptor>(json, JsonOptions);
                    if (descriptor is not null && descriptor.ProjectId != Guid.Empty && !string.IsNullOrWhiteSpace(descriptor.SourceRoot))
                    {
                        entries.Add(new WorkspaceRegistryEntry(
                            descriptor.ProjectId,
                            descriptor.ProjectName,
                            descriptor.SeriesName,
                            NormalizePath(descriptor.SourceRoot),
                            NormalizePath(subDir),
                            descriptor.CreatedAt,
                            descriptor.UpdatedAt,
                            descriptor.LastOpenedAt ?? descriptor.UpdatedAt,
                            descriptor.IsMigratedFromLegacy));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to read descriptor at '{0}' during registry reconstruction: {1}", descriptorPath, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to enumerate workspaces directory for reconstruction: {0}", ex);
        }

        return new RegistryDocument(1, entries);
    }

    private async Task WriteDocumentUnsynchronizedAsync(
        RegistryDocument document, CancellationToken cancellationToken)
    {
        string filePath = _dataLocation.RegistryFilePath;
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{filePath}.tmp-{Guid.NewGuid():N}";
        await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    private static void BackupCorruptedRegistry(string filePath)
    {
        try
        {
            string backupPath = $"{filePath}.corrupt-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            File.Move(filePath, backupPath, overwrite: true);
        }
        catch
        {
            // Best effort backup
        }
    }

    private sealed record RegistryDocument(
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("workspaces")] List<WorkspaceRegistryEntry> Workspaces);
}
