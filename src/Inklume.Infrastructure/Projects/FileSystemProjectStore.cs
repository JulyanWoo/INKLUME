using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Persistence;
using Inklume.Infrastructure.Workspaces;
using Microsoft.Data.Sqlite;

namespace Inklume.Infrastructure.Projects;

public sealed class FileSystemProjectStore : IProjectStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CreationLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly IWorkspaceRegistry _registry;

    public FileSystemProjectStore()
        : this(new DefaultWorkspaceDataLocation(), null)
    {
    }

    public FileSystemProjectStore(IWorkspaceDataLocation dataLocation)
        : this(dataLocation, null)
    {
    }

    public FileSystemProjectStore(
        IWorkspaceDataLocation dataLocation,
        IWorkspaceRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(dataLocation);
        _dataLocation = dataLocation;
        _registry = registry ?? new JsonWorkspaceRegistry(dataLocation);
    }

    public async Task<ProjectWorkspace> CreateAsync(
        TranslationProject project, string sourceRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedSourceRoot = ProjectPaths.Normalize(sourceRoot);
        SemaphoreSlim createLock = CreationLocks.GetOrAdd(normalizedSourceRoot, _ => new SemaphoreSlim(1, 1));
        await createLock.WaitAsync(cancellationToken);
        try
        {
            return await CreateInternalAsync(project, normalizedSourceRoot, cancellationToken);
        }
        finally
        {
            createLock.Release();
        }
    }

    private async Task<ProjectWorkspace> CreateInternalAsync(
        TranslationProject project, string sourceRoot, CancellationToken cancellationToken)
    {
        if (File.Exists(sourceRoot))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The selected path is a file, not a folder.");
        }

        string? parentPath = Path.GetDirectoryName(sourceRoot);
        if (parentPath is null || !Directory.Exists(parentPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath,
                "The parent folder must exist before creating a project.");
        }

        // Check if a workspace already exists for this exact source root in registry
        WorkspaceRegistryEntry? existingForSource = await _registry.FindBySourceRootAsync(sourceRoot, cancellationToken);
        if (existingForSource is not null)
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "A workspace is already registered for this folder. Open the existing workspace instead.");
        }

        string dataRoot = _dataLocation.GetDataRoot(project.Id);
        if (Directory.Exists(dataRoot))
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "A workspace data directory already exists for this project ID.");
        }

        bool createdSourceRoot = false;
        if (!Directory.Exists(sourceRoot))
        {
            Directory.CreateDirectory(sourceRoot);
            createdSourceRoot = true;
        }

        var createdDataDirectories = new List<string>();
        var createdDataFiles = new List<string>();

        try
        {
            Directory.CreateDirectory(dataRoot);
            createdDataDirectories.Add(dataRoot);

            string contextPath = Path.Combine(dataRoot, "context");
            Directory.CreateDirectory(contextPath);
            createdDataDirectories.Add(contextPath);

            string cachePath = Path.Combine(dataRoot, "cache");
            Directory.CreateDirectory(cachePath);
            createdDataDirectories.Add(cachePath);

            cancellationToken.ThrowIfCancellationRequested();

            string databasePath = Path.Combine(dataRoot, ProjectPaths.DatabaseFileName);
            await SqliteProjectPersistence.InitializeAsync(databasePath, cancellationToken);
            createdDataFiles.Add(databasePath);

            await ProjectContextFiles.CreateAsync(dataRoot, project, cancellationToken);
            createdDataFiles.Add(Path.Combine(contextPath, "series.json"));
            createdDataFiles.Add(Path.Combine(contextPath, "characters.json"));
            createdDataFiles.Add(Path.Combine(contextPath, "glossary.json"));
            createdDataFiles.Add(Path.Combine(contextPath, "translation_rules.json"));

            await SqliteProjectPersistence.SaveAsync(databasePath, project, cancellationToken);

            var descriptor = new WorkspaceDescriptor(
                ProjectContextFiles.FormatVersion,
                project.Id,
                project.Name,
                project.SeriesName,
                sourceRoot,
                project.CreatedAt,
                project.UpdatedAt,
                project.UpdatedAt,
                IsMigratedFromLegacy: false);
            await WriteWorkspaceDescriptorAsync(dataRoot, descriptor, cancellationToken);
            createdDataFiles.Add(Path.Combine(dataRoot, "workspace.json"));

            var registryEntry = new WorkspaceRegistryEntry(
                project.Id,
                project.Name,
                project.SeriesName,
                sourceRoot,
                dataRoot,
                project.CreatedAt,
                project.UpdatedAt,
                project.UpdatedAt,
                IsLegacyMigrated: false);
            await _registry.RegisterOrUpdateAsync(registryEntry, cancellationToken);

            return new ProjectWorkspace(project, sourceRoot, dataRoot);
        }
        catch (Exception exception)
        {
            RollbackDataResources(createdDataFiles, createdDataDirectories);
            if (createdSourceRoot && Directory.Exists(sourceRoot)
                && !Directory.EnumerateFileSystemEntries(sourceRoot).Any())
            {
                try
                {
                    Directory.Delete(sourceRoot);
                }
                catch
                {
                    // Best effort
                }
            }

            if (exception is ProjectOperationException or OperationCanceledException)
            {
                throw;
            }

            if (exception is UnauthorizedAccessException)
            {
                throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                    "INKLUME cannot write to the workspace data folder.", exception);
            }

            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The project could not be created. Pre-existing files were preserved.", exception);
        }
    }

    public async Task<ProjectWorkspace> OpenAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string normalizedSourceRoot = ProjectPaths.Normalize(sourceRoot);
            if (!Directory.Exists(normalizedSourceRoot))
            {
                throw new ProjectOperationException(ProjectErrorCode.NotFound,
                    "The selected folder no longer exists.");
            }

            if ((File.GetAttributes(normalizedSourceRoot) & FileAttributes.Directory) == 0)
            {
                throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The selected path is not a folder.");
            }

            // Verify readability without altering the folder
            try
            {
                _ = Directory.EnumerateFileSystemEntries(normalizedSourceRoot).Take(1).ToList();
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                    "INKLUME cannot read the selected folder.", exception);
            }

            // 1. Check if already registered in WorkspaceRegistry (existing external workspace)
            WorkspaceRegistryEntry? entry = await _registry.FindBySourceRootAsync(normalizedSourceRoot, cancellationToken);
            if (entry is not null && Directory.Exists(entry.DataRoot))
            {
                string databasePath = Path.Combine(entry.DataRoot, ProjectPaths.DatabaseFileName);
                if (File.Exists(databasePath))
                {
                    TranslationProject existingProject = await SqliteProjectPersistence.ReadAsync(databasePath, cancellationToken);
                    await ProjectContextFiles.ValidateAsync(entry.DataRoot, existingProject, cancellationToken);

                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    var updatedEntry = entry with { LastOpenedAt = now };
                    await _registry.RegisterOrUpdateAsync(updatedEntry, cancellationToken);

                    return new ProjectWorkspace(existingProject, normalizedSourceRoot, entry.DataRoot);
                }
            }

            // 2. Check for legacy project format inside sourceRoot ONLY IF not yet registered
            string legacyDbPath = Path.Combine(normalizedSourceRoot, ProjectPaths.LegacyDatabaseFileName);
            string legacyContextPath = Path.Combine(normalizedSourceRoot, "context");
            bool isLegacy = File.Exists(legacyDbPath) && Directory.Exists(legacyContextPath);

            if (isLegacy)
            {
                return await HandleLegacyProjectAsync(normalizedSourceRoot, legacyDbPath, legacyContextPath, cancellationToken);
            }

            // 3. First open of unknown folder: initialize external DataRoot without touching source folder
            return await InitializeNewWorkspaceForFolderAsync(sourceRoot, cancellationToken);
        }
        catch (ProjectOperationException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "INKLUME cannot access the workspace data or source folder.", exception);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ProjectOperationException(ProjectErrorCode.NotFound,
                "The workspace folder or one of its files no longer exists.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 3 or 8 or 14)
        {
            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "The workspace database cannot be accessed. Check permissions.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The workspace database is busy. Close the other operation and try again.", exception);
        }
        catch (Exception exception) when (exception is JsonException or SqliteException or FormatException
            or InvalidOperationException or OverflowException or InvalidCastException)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The workspace contains invalid or incomplete data.", exception);
        }
        catch (IOException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The workspace files could not be read. Check folder access.", exception);
        }
    }

    private async Task<ProjectWorkspace> HandleLegacyProjectAsync(
        string sourceRoot, string legacyDbPath, string legacyContextPath, CancellationToken cancellationToken)
    {
        // Probe legacy database to read the project identity
        TranslationProject legacyProject = await SqliteProjectPersistence.ReadAsync(legacyDbPath, cancellationToken);

        string dataRoot = _dataLocation.GetDataRoot(legacyProject.Id);
        string workspaceDbPath = Path.Combine(dataRoot, ProjectPaths.DatabaseFileName);
        string contextPath = Path.Combine(dataRoot, "context");
        string cachePath = Path.Combine(dataRoot, "cache");

        // Idempotent migration: only copy if workspace.db does not already exist
        if (!File.Exists(workspaceDbPath))
        {
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(contextPath);
            Directory.CreateDirectory(cachePath);

            // Copy project.db to external workspace.db
            File.Copy(legacyDbPath, workspaceDbPath, overwrite: false);

            // Copy context files
            foreach (string fileName in new[] { "series.json", "characters.json", "glossary.json", "translation_rules.json" })
            {
                string sourceFile = Path.Combine(legacyContextPath, fileName);
                string destFile = Path.Combine(contextPath, fileName);
                if (File.Exists(sourceFile) && !File.Exists(destFile))
                {
                    File.Copy(sourceFile, destFile, overwrite: false);
                }
            }

            var descriptor = new WorkspaceDescriptor(
                ProjectContextFiles.FormatVersion,
                legacyProject.Id,
                legacyProject.Name,
                legacyProject.SeriesName,
                sourceRoot,
                legacyProject.CreatedAt,
                legacyProject.UpdatedAt,
                DateTimeOffset.UtcNow,
                IsMigratedFromLegacy: true);
            await WriteWorkspaceDescriptorAsync(dataRoot, descriptor, cancellationToken);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var entry = new WorkspaceRegistryEntry(
            legacyProject.Id,
            legacyProject.Name,
            legacyProject.SeriesName,
            sourceRoot,
            dataRoot,
            legacyProject.CreatedAt,
            legacyProject.UpdatedAt,
            now,
            IsLegacyMigrated: true);
        await _registry.RegisterOrUpdateAsync(entry, cancellationToken);

        // Reopen from migrated external dataRoot
        TranslationProject project = await SqliteProjectPersistence.ReadAsync(workspaceDbPath, cancellationToken);
        await ProjectContextFiles.ValidateAsync(dataRoot, project, cancellationToken);

        return new ProjectWorkspace(project, sourceRoot, dataRoot);
    }

    private async Task<ProjectWorkspace> InitializeNewWorkspaceForFolderAsync(
        string sourceRoot, CancellationToken cancellationToken)
    {
        string folderName = Path.GetFileName(sourceRoot);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "Series";
        }

        Guid projectId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var project = new TranslationProject(projectId, folderName, folderName, now, now);

        string dataRoot = _dataLocation.GetDataRoot(projectId);
        Directory.CreateDirectory(dataRoot);

        string contextPath = Path.Combine(dataRoot, "context");
        Directory.CreateDirectory(contextPath);

        string cachePath = Path.Combine(dataRoot, "cache");
        Directory.CreateDirectory(cachePath);

        string databasePath = Path.Combine(dataRoot, ProjectPaths.DatabaseFileName);
        await SqliteProjectPersistence.InitializeAsync(databasePath, cancellationToken);
        await ProjectContextFiles.CreateAsync(dataRoot, project, cancellationToken);
        await SqliteProjectPersistence.SaveAsync(databasePath, project, cancellationToken);

        var descriptor = new WorkspaceDescriptor(
            ProjectContextFiles.FormatVersion,
            projectId,
            folderName,
            folderName,
            sourceRoot,
            now,
            now,
            now,
            IsMigratedFromLegacy: false);
        await WriteWorkspaceDescriptorAsync(dataRoot, descriptor, cancellationToken);

        var registryEntry = new WorkspaceRegistryEntry(
            projectId,
            folderName,
            folderName,
            sourceRoot,
            dataRoot,
            now,
            now,
            now,
            IsLegacyMigrated: false);
        await _registry.RegisterOrUpdateAsync(registryEntry, cancellationToken);

        return new ProjectWorkspace(project, sourceRoot, dataRoot);
    }

    private static async Task WriteWorkspaceDescriptorAsync(
        string dataRoot, WorkspaceDescriptor descriptor, CancellationToken cancellationToken)
    {
        string path = Path.Combine(dataRoot, "workspace.json");
        string tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, descriptor, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static void RollbackDataResources(List<string> createdFiles, List<string> createdDirectories)
    {
        SqliteConnection.ClearAllPools();

        foreach (string filePath in createdFiles)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                string walPath = $"{filePath}-wal";
                if (File.Exists(walPath))
                {
                    File.Delete(walPath);
                }

                string shmPath = $"{filePath}-shm";
                if (File.Exists(shmPath))
                {
                    File.Delete(shmPath);
                }
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("Failed to delete created file during rollback: {0}", exception);
            }
        }

        for (int i = createdDirectories.Count - 1; i >= 0; i--)
        {
            string dirPath = createdDirectories[i];
            try
            {
                if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    Directory.Delete(dirPath);
                }
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("Failed to delete created directory during rollback: {0}", exception);
            }
        }
    }
}
